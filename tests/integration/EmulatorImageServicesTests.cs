using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using FluentAssertions;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.IntegrationTests;

public class EmulatorImageServicesTests
{
    [Fact]
    public void CaptureSessionStoreTrimsOldest()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new CaptureSessionStore(maxEntries: 1);
        var first = store.Add(CreateTestPng(Color.Red));
        Thread.Sleep(5);
        var second = store.Add(CreateTestPng(Color.Blue));

        store.TryGet(first.Id, out _).Should().BeFalse();
        store.TryGet(second.Id, out _).Should().BeTrue();
    }

    [Fact]
    public void ImageCaptureMetricsReportsCountsAndP95()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var metrics = new ImageCaptureMetrics(loggerFactory.CreateLogger<ImageCaptureMetrics>());

        metrics.RecordCaptureResult(120, success: true, withinOnePixel: false);
        metrics.RecordCaptureResult(200, success: false, withinOnePixel: true);

        metrics.SuccessCount.Should().Be(1);
        metrics.FailureCount.Should().Be(1);
        metrics.AccuracyFailures.Should().Be(1);
        metrics.LastDurationMs.Should().Be(200);
        metrics.P95DurationMs.Should().Be(200);
    }

    [Fact]
    public void ImageCaptureMetricsReturnsZeroWhenNoSamples()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var metrics = new ImageCaptureMetrics(loggerFactory.CreateLogger<ImageCaptureMetrics>());

        metrics.P95DurationMs.Should().Be(0);
        metrics.SuccessCount.Should().Be(0);
        metrics.FailureCount.Should().Be(0);
        metrics.AccuracyFailures.Should().Be(0);
    }

    [Fact]
    public void ImageCropperThrowsWhenCaptureMissing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Action act = () => ImageCropper.Crop(null!, new CropBounds(0, 0, 16, 16));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ImageCropperRejectsTooSmallBounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capture = CreateCapture();
        Action act = () => ImageCropper.Crop(capture, new CropBounds(0, 0, 8, 8));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static CaptureSession CreateCapture()
    {
        var png = CreateTestPng(Color.DarkSlateGray);
        return new CaptureSession("cap", png, 32, 32, DateTimeOffset.UtcNow);
    }

    private static byte[] CreateTestPng(Color fill)
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(fill);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
