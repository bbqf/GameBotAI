using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests;

public sealed class SessionCreationTimeoutTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public SessionCreationTimeoutTests() {
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

  [Fact]
  public async Task ReturnsGatewayTimeoutWhenCreationExceedsConfiguredLimit() {
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = baseFactory.WithWebHostBuilder(builder => {
      builder.ConfigureServices(services => {
        services.AddSingleton<ISessionManager>(_ => new SlowSessionManager(TimeSpan.FromSeconds(2)));
        services.Configure<SessionCreationOptions>(opts => opts.TimeoutSeconds = 1);
      });
    });

    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var resp = await client.PostAsJsonAsync("/api/sessions", new { gameId = "g1", adbSerial = "device-1" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
  }

  [Fact]
  public async Task ReturnsSessionIdOnSuccess() {
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = baseFactory.WithWebHostBuilder(builder => {
      builder.ConfigureServices(services => {
        services.AddSingleton<ISessionManager>(_ => new SlowSessionManager(TimeSpan.Zero));
        services.Configure<SessionCreationOptions>(opts => opts.TimeoutSeconds = 5);
      });
    });

    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var resp = await client.PostAsJsonAsync("/api/sessions", new { gameId = "g1", adbSerial = "device-1" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);

    var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    payload.Should().NotBeNull();
    var sessionId = payload?["sessionId"]?.ToString();
    sessionId.Should().NotBeNullOrWhiteSpace();
  }
}

internal sealed class SlowSessionManager : ISessionManager {
  private readonly TimeSpan _delay;
  private int _created;

  public SlowSessionManager(TimeSpan delay) {
    _delay = delay;
  }

  public int ActiveCount => _created;
  public bool CanCreateSession => true;

  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) {
    if (_delay > TimeSpan.Zero) {
      Thread.Sleep(_delay);
    }
    _created++;
    return new EmulatorSession {
      Id = Guid.NewGuid().ToString("N"),
      GameId = gameIdOrPath,
      Status = SessionStatus.Running,
      DeviceSerial = preferredDeviceSerial ?? string.Empty
    };
  }

  public EmulatorSession? GetSession(string id) => null;
  public IReadOnlyCollection<EmulatorSession> ListSessions() => Array.Empty<EmulatorSession>();
  public bool StopSession(string id) => false;
  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}
