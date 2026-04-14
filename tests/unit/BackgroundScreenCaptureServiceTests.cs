using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests;

public sealed class BackgroundScreenCaptureServiceTests : IDisposable
{
    private readonly BackgroundScreenCaptureService _service;

    public BackgroundScreenCaptureServiceTests()
    {
        _service = new BackgroundScreenCaptureService(
            serial => new FakeCaptureProvider(),
            100, // 100ms interval for fast tests
            NullLogger<BackgroundScreenCaptureService>.Instance);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task StartCaptureCreatesLoopAndCapturesFrame()
    {
        _service.StartCapture("sess-1", "device-1");

        // Wait for at least one capture cycle
        await Task.Delay(300);

        var frame = _service.GetCachedFrame("sess-1");
        frame.Should().NotBeNull();
        frame!.PngBytes.Should().NotBeEmpty();
        frame.Width.Should().BeGreaterThan(0);
        frame.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetCachedFrameReturnsNullBeforeFirstCapture()
    {
        // No loop started
        _service.GetCachedFrame("nonexistent").Should().BeNull();
    }

    [Fact]
    public async Task StopCaptureCancelsLoopAndDisposesResources()
    {
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(300);

        _service.StopCapture("sess-1");

        _service.GetCachedFrame("sess-1").Should().BeNull();
        _service.GetCaptureMetrics("sess-1").Should().BeNull();
    }

    [Fact]
    public async Task GetCaptureMetricsReturnsRollingFps()
    {
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(800); // Allow several capture cycles at 100ms interval

        var metrics = _service.GetCaptureMetrics("sess-1");
        metrics.Should().NotBeNull();
        metrics!.FrameCount.Should().BeGreaterThan(0);
        metrics.CaptureRateFps.Should().NotBeNull().And.BeGreaterThan(0);
        metrics.LastCaptureUtc.Should().NotBeNull();
    }

    [Fact]
    public void GetCaptureMetricsReturnsNullForUnknownSession()
    {
        _service.GetCaptureMetrics("nonexistent").Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentReadsDoNotBlock()
    {
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(300);

        var sw = Stopwatch.StartNew();
        var tasks = new Task[10];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => _service.GetCachedFrame("sess-1"));
        }
        await Task.WhenAll(tasks);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100, "concurrent reads should complete instantly");
    }

    [Fact]
    public async Task StopAllDisposesAllLoops()
    {
        _service.StartCapture("sess-1", "device-1");
        _service.StartCapture("sess-2", "device-2");
        await Task.Delay(300);

        _service.StopAll();

        _service.GetCachedFrame("sess-1").Should().BeNull();
        _service.GetCachedFrame("sess-2").Should().BeNull();
    }

    [Fact]
    public async Task DuplicateStartCaptureStopsOldLoopFirst()
    {
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(300);

        var firstFrame = _service.GetCachedFrame("sess-1");
        firstFrame.Should().NotBeNull();

        // Restart with a different device
        _service.StartCapture("sess-1", "device-2");
        await Task.Delay(300);

        var secondFrame = _service.GetCachedFrame("sess-1");
        secondFrame.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCachedFrameCompletesUnder5Ms()
    {
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(300);

        var sw = Stopwatch.StartNew();
        _ = _service.GetCachedFrame("sess-1");
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(5, "SC-001: cached frame retrieval must be near-instant");
    }

    [Fact]
    public async Task FailingProviderContinuesCapturing()
    {
        var provider = new FailThenSucceedProvider(failCount: 2);
        using var service = new BackgroundScreenCaptureService(
            _ => provider,
            50,
            NullLogger<BackgroundScreenCaptureService>.Instance);

        service.StartCapture("sess-1", "device-1");
        await Task.Delay(500);

        var frame = service.GetCachedFrame("sess-1");
        frame.Should().NotBeNull("capture should succeed after transient failures");
    }

    [Fact]
    public void StopCaptureNoOpForUnknownSession()
    {
        // Should not throw
        _service.StopCapture("nonexistent");
    }
}

file sealed class FakeCaptureProvider : IAdbScreenCaptureProvider
{
    // 1x1 red PNG
    private static readonly byte[] FakePng = CreateMinimalPng();

    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct)
    {
        return Task.FromResult<byte[]?>(FakePng);
    }

    private static byte[] CreateMinimalPng()
    {
        // Create a tiny 1x1 Bitmap and encode as PNG
        using var bmp = new System.Drawing.Bitmap(2, 2);
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}

file sealed class FailThenSucceedProvider : IAdbScreenCaptureProvider
{
    private int _callCount;
    private readonly int _failCount;

    public FailThenSucceedProvider(int failCount) => _failCount = failCount;

    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct)
    {
        if (Interlocked.Increment(ref _callCount) <= _failCount)
            throw new InvalidOperationException("Simulated ADB failure");

        using var bmp = new System.Drawing.Bitmap(2, 2);
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Task.FromResult<byte[]?>(ms.ToArray());
    }
}
