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

namespace GameBot.ContractTests.Sessions;

public sealed class SessionsContractsTests : IDisposable
{
    private readonly string? _prevAuthToken;
    private readonly string? _prevUseAdb;
    private readonly string? _prevDynamicPort;

    public SessionsContractsTests()
    {
        _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
        _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
        _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RunningStartStopEndpointsAreExposed()
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

        var runningResp = await client.GetAsync(new Uri("/api/sessions/running", UriKind.Relative)).ConfigureAwait(true);
        runningResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var startResp = await client.PostAsJsonAsync(new Uri("/api/sessions/start", UriKind.Relative), new { gameId = "game-1", emulatorId = "emu-1" }).ConfigureAwait(true);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var startPayload = await startResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        startPayload.Should().NotBeNull();
        var sessionId = startPayload!["sessionId"]!.ToString();
        sessionId.Should().NotBeNullOrWhiteSpace();

        var stopResp = await client.PostAsJsonAsync(new Uri("/api/sessions/stop", UriKind.Relative), new { sessionId }).ConfigureAwait(true);
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        private readonly List<EmulatorSession> _sessions = new();

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
            if (session is null) return false;
            _sessions.Remove(session);
            return true;
        }

        public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
        public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
    }
}
