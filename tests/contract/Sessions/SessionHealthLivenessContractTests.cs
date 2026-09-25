#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
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
/// Feature 106 (FR-007 to FR-009, contract <c>session-health.md</c>): the <c>liveness</c> block of
/// <c>GET /api/sessions/{id}/health</c>.
/// </summary>
public sealed class SessionHealthLivenessContractTests : IDisposable {
  private static readonly string[] LivenessFields =
    { "state", "reason", "frameAgeMs", "unchangedMs", "stale", "lastInputAt", "lastInputOutcome" };

  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public SessionHealthLivenessContractTests() {
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

  private static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static WebApplicationFactory<Program> Factory(WebApplicationFactory<Program> baseFactory, Action<IServiceCollection> configure) =>
    baseFactory.WithWebHostBuilder(b => b.ConfigureTestServices(configure));

  private static async Task<JsonElement> GetHealthAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/sessions/{id}/health", UriKind.Relative));
    var body = await resp.Content.ReadAsStringAsync();
    resp.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return JsonDocument.Parse(body).RootElement.Clone();
  }

  [Fact]
  public async Task AStubSessionReportsUnknownWithAllSevenFields() {
    using var app = new WebApplicationFactory<Program>();
    var client = Client(app);
    var created = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = "g-liveness" });
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

    var health = await GetHealthAsync(client, id);

    health.GetProperty("id").GetString().Should().Be(id);
    health.GetProperty("mode").GetString().Should().Be("STUB");
    health.GetProperty("deviceSerial").ValueKind.Should().Be(JsonValueKind.Null);
    health.GetProperty("adb").GetProperty("ok").GetBoolean().Should().BeTrue();
    var liveness = health.GetProperty("liveness");
    foreach (var field in LivenessFields) liveness.TryGetProperty(field, out _).Should().BeTrue(field);
    liveness.GetProperty("state").GetString().Should().Be("unknown");
    liveness.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
    liveness.GetProperty("stale").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task ATransportFaultGivesNotLiveTransportNotReady() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emu-liveness-1");
    var transport = new FixedTransportCheck { Result = new SessionTransportCheckResult(false, "offline", string.Empty, null) };
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      services.RemoveAll<ISessionTransportCheck>();
      services.AddSingleton<ISessionTransportCheck>(transport);
      services.RemoveAll<ISessionDirectCapture>();
      services.AddSingleton<ISessionDirectCapture>(new FixedDirectCapture());
    });
    var client = Client(app);

    var health = await GetHealthAsync(client, session.Id);

    health.GetProperty("mode").GetString().Should().Be("ADB");
    health.GetProperty("deviceSerial").GetString().Should().Be("emu-liveness-1");
    health.GetProperty("adb").GetProperty("ok").GetBoolean().Should().BeFalse();
    health.GetProperty("adb").GetProperty("stdout").GetString().Should().Be("offline");
    health.GetProperty("liveness").GetProperty("state").GetString().Should().Be("not_live");
    health.GetProperty("liveness").GetProperty("reason").GetString().Should().Be("transport_not_ready");
  }

  [Fact]
  public async Task NoChangeAfterInputIsReported() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emu-liveness-2");
    var now = DateTimeOffset.UtcNow;
    var tracker = new FixedSampleTracker {
      Now = now,
      SampleFactory = (hasDevice, transportReady) => new DeviceLivenessSample(
        hasDevice, transportReady, true, now.AddMinutes(-30), now.AddMilliseconds(-480), now.AddMinutes(-10),
        now.AddMinutes(-6), now.AddSeconds(-3), InputOutcome.Completed)
    };
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      services.RemoveAll<ISessionTransportCheck>();
      services.AddSingleton<ISessionTransportCheck>(new FixedTransportCheck());
      services.RemoveAll<IDeviceLivenessTracker>();
      services.AddSingleton<IDeviceLivenessTracker>(tracker);
    });
    var client = Client(app);

    var liveness = (await GetHealthAsync(client, session.Id)).GetProperty("liveness");

    liveness.GetProperty("state").GetString().Should().Be("not_live");
    liveness.GetProperty("reason").GetString().Should().Be("no_change_after_input");
    liveness.GetProperty("frameAgeMs").GetInt64().Should().Be(480);
    liveness.GetProperty("unchangedMs").GetInt64().Should().Be(600000);
    liveness.GetProperty("stale").GetBoolean().Should().BeTrue();
    liveness.GetProperty("lastInputOutcome").GetString().Should().Be("completed");
    liveness.GetProperty("lastInputAt").GetDateTimeOffset().Should().BeCloseTo(now.AddSeconds(-3), TimeSpan.FromMilliseconds(1));
  }

  [Fact]
  public async Task AnUnknownSessionStillGives404() {
    using var app = new WebApplicationFactory<Program>();
    var client = Client(app);

    var resp = await client.GetAsync(new Uri("/api/sessions/does-not-exist/health", UriKind.Relative));

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement
      .GetProperty("error").GetProperty("code").GetString().Should().Be("not_found");
  }
}
