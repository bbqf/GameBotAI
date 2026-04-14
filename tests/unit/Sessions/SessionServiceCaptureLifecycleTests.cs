using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Sessions;

public sealed class SessionServiceCaptureLifecycleTests : IDisposable
{
    private readonly BackgroundScreenCaptureService _captureService;
    private readonly TestSessionManager _manager;
    private readonly SessionService _service;

    public SessionServiceCaptureLifecycleTests()
    {
        _captureService = new BackgroundScreenCaptureService(
            _ => new NullProvider(), 500, NullLogger<BackgroundScreenCaptureService>.Instance);
        _manager = new TestSessionManager();
        _service = new SessionService((ISessionManager)_manager, new NoOpSessionContextCache(), _captureService);
    }

    public void Dispose() => _captureService.Dispose();

    [Fact]
    public void StartSessionStartsCaptureWhenDeviceSerialAvailable()
    {
        _manager.NextCreateId = "sess-1";
        _service.StartSession("game-1", "emu-1");

        // Capture loop should have been started — metrics object should exist
        _captureService.GetCaptureMetrics("sess-1").Should().NotBeNull();
    }

    [Fact]
    public void StopSessionStopsCapture()
    {
        _manager.NextCreateId = "sess-1";
        _service.StartSession("game-1", "emu-1");
        _captureService.GetCaptureMetrics("sess-1").Should().NotBeNull();

        _service.StopSession("sess-1");

        _captureService.GetCaptureMetrics("sess-1").Should().BeNull();
    }

    [Fact]
    public void SyncFromSessionManagerStopsCaptureForEvictedSessions()
    {
        var seeded = _manager.Seed(new EmulatorSession
        {
            Id = "sess-old",
            GameId = "game-1",
            DeviceSerial = "emu-1",
            Status = SessionStatus.Running,
            StartTime = DateTimeOffset.UtcNow,
            LastActivity = DateTimeOffset.UtcNow
        });

        // Force sync to pick up the session
        _service.GetRunningSessions().Should().ContainSingle();

        // Mark stopped
        seeded.Status = SessionStatus.Stopped;
        _service.GetRunningSessions().Should().BeEmpty();

        // Capture loop should have been stopped
        _captureService.GetCaptureMetrics("sess-old").Should().BeNull();
    }

    [Fact]
    public void StartSessionStopsOldCaptureWhenReplacingExisting()
    {
        _manager.NextCreateId = "sess-old";
        _service.StartSession("game-1", "emu-1");
        _captureService.GetCaptureMetrics("sess-old").Should().NotBeNull();

        _manager.NextCreateId = "sess-new";
        _service.StartSession("game-1", "emu-1");

        _captureService.GetCaptureMetrics("sess-old").Should().BeNull();
        _captureService.GetCaptureMetrics("sess-new").Should().NotBeNull();
    }

    [Fact]
    public void SessionServiceWorksWithoutCaptureService()
    {
        var manager = new TestSessionManager { NextCreateId = "sess-1" };
        var service = new SessionService(manager, new NoOpSessionContextCache());

        var running = service.StartSession("game-1", "emu-1");
        running.SessionId.Should().Be("sess-1");

        service.StopSession("sess-1").Should().BeTrue();
    }
}

file sealed class NullProvider : IAdbScreenCaptureProvider
{
    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult<byte[]?>(null);
}

internal sealed class TestSessionManager : ISessionManager
{
    private readonly Dictionary<string, EmulatorSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public string? NextCreateId { get; set; }
    public string? LastStopSessionId { get; private set; }
    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => true;

    public EmulatorSession Seed(EmulatorSession session)
    {
        _sessions[session.Id] = session;
        return session;
    }

    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null)
    {
        var id = NextCreateId ?? Guid.NewGuid().ToString("N");
        var session = new EmulatorSession
        {
            Id = id,
            GameId = gameIdOrPath,
            DeviceSerial = preferredDeviceSerial,
            Status = SessionStatus.Running,
            StartTime = DateTimeOffset.UtcNow,
            LastActivity = DateTimeOffset.UtcNow
        };
        _sessions[id] = session;
        return session;
    }

    public EmulatorSession? GetSession(string id) => _sessions.GetValueOrDefault(id);
    public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions.Values.ToList();
    public bool StopSession(string id)
    {
        LastStopSessionId = id;
        return _sessions.Remove(id);
    }

    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

internal sealed class NoOpSessionContextCache : ISessionContextCache
{
    public void SetSessionId(string gameId, string adbSerial, string sessionId) { }
    public string? GetSessionId(string gameId, string adbSerial) => null;
    public void ClearSession(string gameId, string adbSerial) { }
}
