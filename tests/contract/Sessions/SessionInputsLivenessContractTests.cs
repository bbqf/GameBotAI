#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.Liveness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameBot.ContractTests.Sessions;

/// <summary>
/// Feature 106 (FR-012, FR-013, SC-007, contract <c>session-inputs.md</c>): the rule order of
/// <c>POST /api/sessions/{id}/inputs</c> after the dispatch: 409, 504, 503, 400, 202.
/// </summary>
public sealed class SessionInputsLivenessContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public SessionInputsLivenessContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    GC.SuppressFinalize(this);
  }

  private sealed class Host : IDisposable {
    private readonly WebApplicationFactory<Program> _base = new();
    public LivenessFakeSessionManager Sessions { get; } = new();
    public FixedLivenessService Liveness { get; } = new();
    public WebApplicationFactory<Program> App { get; }
    public HttpClient Client { get; }

    public Host() {
      App = _base.WithWebHostBuilder(b => b.ConfigureTestServices(services => {
        services.RemoveAll<ISessionManager>();
        services.AddSingleton<ISessionManager>(Sessions);
        services.RemoveAll<ISessionLivenessService>();
        services.AddSingleton<ISessionLivenessService>(Liveness);
      }));
      Client = App.CreateClient();
      Client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    }

    public void Dispose() {
      Client.Dispose();
      App.Dispose();
      _base.Dispose();
    }
  }

  private static readonly object TwoTaps = new {
    actions = new[] {
      new { type = "tap", args = new { x = 1, y = 2 } },
      new { type = "tap", args = new { x = 3, y = 4 } }
    }
  };

  private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string id, object body) {
    var resp = await client.PostAsJsonAsync(new Uri($"/api/sessions/{id}/inputs", UriKind.Relative), body);
    var text = await resp.Content.ReadAsStringAsync();
    return (resp.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
  }

  [Fact]
  public async Task NoActionsGive400AndNoDispatch() {
    using var host = new Host();
    var session = host.Sessions.Add("emu-in-1");

    var (status, body) = await PostAsync(host.Client, session.Id, new { actions = Array.Empty<object>() });

    status.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    host.Sessions.DispatchCalls.Should().Be(0);
  }

  [Fact]
  public async Task AnUnknownSessionGives409() {
    using var host = new Host();

    var (status, body) = await PostAsync(host.Client, "missing", TwoTaps);

    status.Should().Be(HttpStatusCode.Conflict);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("not_running");
  }

  [Fact]
  public async Task ATimedOutActionGives504AndKeepsTheEarlierResults() {
    using var host = new Host();
    var session = host.Sessions.Add("emu-in-2");
    host.Liveness.Report = FixedLivenessService.NotLive(DeviceLivenessReasons.InputTimeout);
    host.Sessions.Dispatch = _ => new SessionInputDispatchResult(true, new[] {
      new InputActionResult(0, true, null),
      new InputActionResult(1, false, "tap: device did not answer in 10000 ms", TimedOut: true)
    });

    var (status, body) = await PostAsync(host.Client, session.Id, TwoTaps);

    status.Should().Be(HttpStatusCode.GatewayTimeout);
    var error = body.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("device_timeout");
    error.GetProperty("message").GetString().Should().Contain("action 1");
    error.GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
    var results = body.GetProperty("results").EnumerateArray().ToList();
    results.Should().HaveCount(2);
    results[0].GetProperty("dispatched").GetBoolean().Should().BeTrue("the 504 rule keeps the earlier dispatched values (FR-013)");
    results[1].GetProperty("dispatched").GetBoolean().Should().BeFalse();
    results[1].GetProperty("failureReason").GetString().Should().Be("tap: device did not answer in 10000 ms");
  }

  [Fact]
  public async Task DispatchedResultsOnADeviceThatIsNotLiveGive503() {
    using var host = new Host();
    var session = host.Sessions.Add("emu-in-3");
    host.Liveness.Report = FixedLivenessService.NotLive(DeviceLivenessReasons.NoChangeAfterInput);

    var (status, body) = await PostAsync(host.Client, session.Id, TwoTaps);

    status.Should().Be(HttpStatusCode.ServiceUnavailable);
    var error = body.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("device_not_live");
    error.GetProperty("reason").GetString().Should().Be("no_change_after_input");
    error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    host.Sessions.DispatchCalls.Should().Be(1, "the service still sends the inputs");
    foreach (var r in body.GetProperty("results").EnumerateArray()) {
      r.GetProperty("dispatched").GetBoolean().Should().BeFalse();
      r.GetProperty("failureReason").GetString().Should().Be("device_not_live: no_change_after_input");
    }
  }

  [Fact]
  public async Task ALiveDeviceGives202WithTheCurrentBody() {
    using var host = new Host();
    var session = host.Sessions.Add("emu-in-4");

    var (status, body) = await PostAsync(host.Client, session.Id, TwoTaps);

    status.Should().Be(HttpStatusCode.Accepted);
    body.GetProperty("accepted").GetInt32().Should().Be(2);
    body.GetProperty("results").GetArrayLength().Should().Be(2);
    body.TryGetProperty("error", out _).Should().BeFalse();
  }

  [Fact]
  public async Task NoDispatchedActionGives400EvenWhenNotLive() {
    using var host = new Host();
    var session = host.Sessions.Add("emu-in-5");
    host.Liveness.Report = FixedLivenessService.NotLive(DeviceLivenessReasons.CaptureStalled);
    host.Sessions.Dispatch = _ => new SessionInputDispatchResult(true, new[] {
      new InputActionResult(0, false, "tap: missing required argument 'x'"),
      new InputActionResult(1, false, "tap: missing required argument 'x'")
    });

    var (status, body) = await PostAsync(host.Client, session.Id, TwoTaps);

    status.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_input_actions");
  }
}
