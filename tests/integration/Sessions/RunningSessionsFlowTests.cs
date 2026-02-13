using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameBot.IntegrationTests.Sessions;

[Collection("ConfigIsolation")]
public sealed class RunningSessionsFlowTests : IDisposable
{
    private readonly string? _prevUseAdb;
    private readonly string? _prevDynamicPort;
    private readonly string? _prevAuthToken;
    private readonly string? _prevDataDir;

    public RunningSessionsFlowTests()
    {
        _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
        _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
        _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
        _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        TestEnvironment.PrepareCleanDataDir();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
        Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task StartReplacesExistingSessionEvenIfStopFails()
    {
        var fakeSessions = new FakeSessionManager();
        using var baseFactory = new WebApplicationFactory<Program>();
        using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(_ => fakeSessions);
            });
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var firstStart = await client.PostAsJsonAsync(new Uri("/api/sessions/start", UriKind.Relative), new { gameId = "game-1", emulatorId = "emu-1" }).ConfigureAwait(true);
        firstStart.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPayload = await firstStart.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var firstId = firstPayload!["sessionId"]!.ToString();
        fakeSessions.FailStopFor(firstId!);

        var secondStart = await client.PostAsJsonAsync(new Uri("/api/sessions/start", UriKind.Relative), new { gameId = "game-1", emulatorId = "emu-1" }).ConfigureAwait(true);
        secondStart.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondPayload = await secondStart.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var secondId = secondPayload!["sessionId"]!.ToString();

        secondId.Should().NotBeNullOrWhiteSpace();
        secondId.Should().NotBe(firstId);

        var runningResp = await client.GetAsync(new Uri("/api/sessions/running", UriKind.Relative)).ConfigureAwait(true);
        var runningPayload = await runningResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var runningSessions = (runningPayload!["sessions"] as System.Text.Json.JsonElement?)?.EnumerateArray().ToArray();
        runningSessions.Should().NotBeNull();
        runningSessions!.Length.Should().Be(1);
        runningSessions[0].GetProperty("sessionId").GetString().Should().Be(secondId);
    }

    [Fact]
    public async Task StopFailureReturnsNotFoundAndClearsList()
    {
        var fakeSessions = new FakeSessionManager();
        using var baseFactory = new WebApplicationFactory<Program>();
        using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager>(_ => fakeSessions);
            });
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var startResp = await client.PostAsJsonAsync(new Uri("/api/sessions/start", UriKind.Relative), new { gameId = "game-2", emulatorId = "emu-2" }).ConfigureAwait(true);
        var startPayload = await startResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var sessionId = startPayload!["sessionId"]!.ToString();
        fakeSessions.FailStopFor(sessionId!);

        var stopResp = await client.PostAsJsonAsync(new Uri("/api/sessions/stop", UriKind.Relative), new { sessionId }).ConfigureAwait(true);
        stopResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var runningResp = await client.GetAsync(new Uri("/api/sessions/running", UriKind.Relative)).ConfigureAwait(true);
        var runningPayload = await runningResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var runningSessions = (runningPayload!["sessions"] as System.Text.Json.JsonElement?)?.EnumerateArray().ToArray();
        runningSessions.Should().NotBeNull();
        runningSessions!.Length.Should().Be(0);
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        private readonly List<EmulatorSession> _sessions = new();
        private readonly HashSet<string> _failStops = new(StringComparer.OrdinalIgnoreCase);

        public int ActiveCount => _sessions.Count;
        public bool CanCreateSession => true;

        public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null)
        {
            var session = new EmulatorSession
            {
                Id = Guid.NewGuid().ToString("N"),
                GameId = gameIdOrPath,
                DeviceSerial = preferredDeviceSerial,
                Status = SessionStatus.Running,
                Health = SessionHealth.Ok,
                StartTime = DateTimeOffset.UtcNow,
                LastActivity = DateTimeOffset.UtcNow
            };
            _sessions.Add(session);
            return session;
        }

        public EmulatorSession? GetSession(string id) => _sessions.FirstOrDefault(s => s.Id == id);
        public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions.ToList();

        public bool StopSession(string id)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == id);
            if (session is not null)
            {
                _sessions.Remove(session);
            }
            return !_failStops.Contains(id);
        }

        public void FailStopFor(string sessionId) => _failStops.Add(sessionId);

        public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
        public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
    }
}
