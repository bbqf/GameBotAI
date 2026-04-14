using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests;

public sealed class CaptureMetricsTests : IDisposable
{
    private readonly BackgroundScreenCaptureService _service;

    public CaptureMetricsTests()
    {
        _service = new BackgroundScreenCaptureService(
            serial => new SmallPngProvider(),
            50, // fast interval
            NullLogger<BackgroundScreenCaptureService>.Instance);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void MetricsAreNullWhenNoLoopExists()
    {
        _service.GetCaptureMetrics("nonexistent").Should().BeNull();
    }

    [Fact]
    public async Task MetricsShowCorrectFrameCount()
    {
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(400); // Allow several capture cycles

        var metrics = _service.GetCaptureMetrics("sess-1");
        metrics.Should().NotBeNull();
        metrics!.FrameCount.Should().BeGreaterThanOrEqualTo(2);
        metrics.LastCaptureUtc.Should().NotBeNull();
        metrics.LastCaptureUtc!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MetricsFpsIsReasonable()
    {
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(600);

        var metrics = _service.GetCaptureMetrics("sess-1");
        metrics.Should().NotBeNull();
        // With 50ms interval, max theoretical FPS is 20; give wide margin for CI
        metrics!.CaptureRateFps.Should().BeGreaterThan(0);
        metrics.CaptureRateFps.Should().BeLessThanOrEqualTo(100, "FPS should be bounded by interval");
    }

    [Fact]
    public async Task MetricsResetAfterStopAndRestart()
    {
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(300);

        var beforeStop = _service.GetCaptureMetrics("sess-1");
        beforeStop.Should().NotBeNull();
        beforeStop!.FrameCount.Should().BeGreaterThan(0);

        _service.StopCapture("sess-1");
        _service.GetCaptureMetrics("sess-1").Should().BeNull();

        // Restart
        _service.StartCapture("sess-1", "device-1");
        await Task.Delay(300);

        var afterRestart = _service.GetCaptureMetrics("sess-1");
        afterRestart.Should().NotBeNull();
        afterRestart!.FrameCount.Should().BeGreaterThan(0);
    }
}

file sealed class SmallPngProvider : IAdbScreenCaptureProvider
{
    private static readonly byte[] Png = CreatePng();

    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) =>
        Task.FromResult<byte[]?>(Png);

    private static byte[] CreatePng()
    {
        using var bmp = new System.Drawing.Bitmap(2, 2);
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}
