using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests;

public sealed class BackgroundCaptureScreenSourceTests : IDisposable
{
    private readonly BackgroundScreenCaptureService _captureService;
    private readonly StubSessionManager _sessions;
    private readonly BackgroundCaptureScreenSource _source;

    public BackgroundCaptureScreenSourceTests()
    {
        _captureService = new BackgroundScreenCaptureService(
            serial => new TinyPngProvider(),
            50,
            NullLogger<BackgroundScreenCaptureService>.Instance);
        _sessions = new StubSessionManager();
        _source = new BackgroundCaptureScreenSource(_captureService, _sessions);
    }

    public void Dispose() => _captureService.Dispose();

    [Fact]
    public void ReturnsNullWhenNoRunningSession()
    {
        _source.GetLatestScreenshot().Should().BeNull();
    }

    [Fact]
    public void ReturnsNullWhenSessionExistsButNoCachedFrame()
    {
        _sessions.AddSession("sess-1", "game-1", "device-1", SessionStatus.Running);
        // No capture loop started
        _source.GetLatestScreenshot().Should().BeNull();
    }

    [Fact]
    public async Task ReturnsBitmapCloneWhenFrameAvailable()
    {
        _sessions.AddSession("sess-1", "game-1", "device-1", SessionStatus.Running);
        _captureService.StartCapture("sess-1", "device-1");
        await Task.Delay(300);

        var bitmap = _source.GetLatestScreenshot();
        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
        bitmap.Dispose();
    }

    [Fact]
    public async Task DoesNotCallAdbDirectly()
    {
        var callTracker = new TrackingPngProvider();
        using var captureService = new BackgroundScreenCaptureService(
            _ => callTracker,
            50,
            NullLogger<BackgroundScreenCaptureService>.Instance);

        _sessions.AddSession("sess-1", "game-1", "device-1", SessionStatus.Running);
        captureService.StartCapture("sess-1", "device-1");
        await Task.Delay(200);

        var source = new BackgroundCaptureScreenSource(captureService, _sessions);        var callsBefore = callTracker.CallCount;
        _ = source.GetLatestScreenshot();
        // Getting screenshot should NOT trigger another ADB call
        callTracker.CallCount.Should().Be(callsBefore);
    }
}

internal sealed class StubSessionManager : ISessionManager
{
    private readonly List<EmulatorSession> _sessions = new();

    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => true;

    public void AddSession(string id, string gameId, string deviceSerial, SessionStatus status)
    {
        _sessions.Add(new EmulatorSession
        {
            Id = id,
            GameId = gameId,
            DeviceSerial = deviceSerial,
            Status = status,
            StartTime = DateTimeOffset.UtcNow,
            LastActivity = DateTimeOffset.UtcNow
        });
    }

    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
    public EmulatorSession? GetSession(string id) => null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions;
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

file sealed class TinyPngProvider : IAdbScreenCaptureProvider
{
    private static readonly byte[] Png = MakePng();
    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult<byte[]?>(Png);
    private static byte[] MakePng()
    {
        using var bmp = new Bitmap(2, 2);
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}

file sealed class TrackingPngProvider : IAdbScreenCaptureProvider
{
    private int _callCount;
    public int CallCount => _callCount;
    private static readonly byte[] Png = MakePng();
    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<byte[]?>(Png);
    }
    private static byte[] MakePng()
    {
        using var bmp = new Bitmap(2, 2);
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}
