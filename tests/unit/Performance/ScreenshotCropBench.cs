using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace GameBot.Tests.Unit.Performance;

public sealed class ScreenshotCropBench
{
    [Fact]
    public void CaptureCropSaveP95UnderOneSecond()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // System.Drawing is only supported on Windows in this repo
        }

        const int width = 1920;
        const int height = 1080;
        using var baseImage = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(baseImage))
        {
            g.Clear(Color.DarkSlateGray);
            g.FillRectangle(Brushes.LightSlateGray, new Rectangle(0, 0, width / 3, height / 3));
        }

        using var seedStream = new MemoryStream();
        baseImage.Save(seedStream, ImageFormat.Png);
        var captureBytes = seedStream.ToArray();
        var crop = new Rectangle(100, 100, 320, 180);

        var durations = new long[5];
        for (var i = 0; i < durations.Length; i++)
        {
            var sw = Stopwatch.StartNew();
            using var captureStream = new MemoryStream(captureBytes, writable: false);
            using var captured = new Bitmap(captureStream);
            using var cropped = captured.Clone(crop, captured.PixelFormat);
            using var output = new MemoryStream();
            cropped.Save(output, ImageFormat.Png);
            sw.Stop();
            durations[i] = (long)sw.Elapsed.TotalMilliseconds;
        }

        var ordered = durations.OrderBy(x => x).ToArray();
        var p95Index = (int)System.Math.Ceiling(0.95 * ordered.Length) - 1;
        var p95 = ordered[System.Math.Max(p95Index, 0)];

        p95.Should().BeLessThan(1000, "capture->crop->save should meet the 1s p95 requirement");
    }
}
