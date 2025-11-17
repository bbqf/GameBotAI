using System;
using System.Drawing;
using System.Drawing.Imaging;
using FluentAssertions;
using GameBot.Domain.Profiles;
using GameBot.Domain.Profiles.Evaluators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using GameBot.UnitTests.Helpers;
using Xunit;

namespace GameBot.UnitTests.Emulator.Triggers;

public class TextMatchEvaluatorImagePipelineTests
{
    public TextMatchEvaluatorImagePipelineTests() { }
    private sealed class CapturingOcr : ITextOcr
    {
        public int LastWidth { get; private set; }
        public int LastHeight { get; private set; }
        public Bitmap? LastImageClone { get; private set; }
        private readonly string _text;
        private readonly double _conf;
        public CapturingOcr(string text = "HELLO", double conf = 0.99)
        {
            _text = text; _conf = conf;
        }
        public OcrResult Recognize(Bitmap image)
        {
            Capture(image);
            return new OcrResult(_text, _conf);
        }
        public OcrResult Recognize(Bitmap image, string? language)
        {
            Capture(image);
            return new OcrResult(_text, _conf);
        }
        private void Capture(Bitmap image)
        {
            LastWidth = image.Width; LastHeight = image.Height;
            // clone to inspect after evaluator disposes its working bitmap
            LastImageClone?.Dispose();
            LastImageClone = new Bitmap(image);
        }
    }

    private static Bitmap MakeScreen(int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        using var pen = new Pen(Color.Black, 1);
        g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
        return bmp;
    }

    [Fact]
    public void CropsRegionToExpectedPixelSize()
    {
        using var screen = MakeScreen(200, 100);
        var screenSource = new SingleBitmapScreenSource(() => new Bitmap(screen));
        var ocr = new CapturingOcr();
        using var provider = new TestLoggerProvider(LogLevel.Debug);
        using var factory = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(provider); });
        var logger = factory.CreateLogger<TextMatchEvaluator>();
        var eval = new TextMatchEvaluator(ocr, screenSource, logger);

        var trig = new ProfileTrigger
        {
            Id = "t1",
            Type = TriggerType.TextMatch,
            Enabled = true,
            Params = new TextMatchParams
            {
                Target = "HELLO",
                Region = new GameBot.Domain.Profiles.Region { X = 0.25, Y = 0.20, Width = 0.50, Height = 0.50 },
                ConfidenceThreshold = 0.10,
                Mode = "found",
                Language = null
            }
        };

        var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
        res.Status.Should().Be(TriggerStatus.Satisfied);

        // Expected crop: 200*0.5 x 100*0.5 = 100 x 50 before preprocessing
        // OCR sees preprocessed image, but width should scale proportionally; height may upscale to >= 50*Scale
        ocr.LastWidth.Should().BeGreaterThan(0);
        ocr.LastHeight.Should().BeGreaterThan(0);
        // Since preprocessing uses integer scaling, assert aspect ratio preserved roughly
        (ocr.LastWidth / (double)ocr.LastHeight).Should().BeApproximately(100 / 50.0, 0.05);

        // Assert the cropped size log is present and not 1x1
        provider.Entries.Should().Contain(e =>
            e.Category.Contains("TextMatchEvaluator") &&
            e.Message.Contains("Cropped image size"));
        provider.Entries.Should().NotContain(e =>
            e.Category.Contains("TextMatchEvaluator") &&
            e.Message.Contains("Cropped image size 1x1"));
    }

    [Fact]
    public void UpscalesSmallCropToAtLeastMinHeight()
    {
        using var screen = MakeScreen(60, 20); // small screen
        var screenSource = new SingleBitmapScreenSource(() => new Bitmap(screen));
        var ocr = new CapturingOcr();
        using var provider = new TestLoggerProvider(LogLevel.Debug);
        using var factory = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(provider); });
        var logger = factory.CreateLogger<TextMatchEvaluator>();
        var eval = new TextMatchEvaluator(ocr, screenSource, logger);

        var trig = new ProfileTrigger
        {
            Id = "t2",
            Type = TriggerType.TextMatch,
            Enabled = true,
            Params = new TextMatchParams
            {
                Target = "HELLO",
                Region = new GameBot.Domain.Profiles.Region { X = 0.0, Y = 0.0, Width = 1.0, Height = 0.2 }, // 60x4 crop
                ConfidenceThreshold = 0.10,
                Mode = "found",
                Language = null
            }
        };

        var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
        res.Status.Should().Be(TriggerStatus.Satisfied);
        // MinHeight in evaluator preprocessing is 32
        ocr.LastHeight.Should().BeGreaterOrEqualTo(32);
    }

    [Fact]
    public void BinarizesPreprocessedImageToBlackAndWhite()
    {
        // Create a screen with varying grayscale so thresholding produces both black and white
        using var screen = new Bitmap(40, 16, PixelFormat.Format24bppRgb);
        for (int y = 0; y < screen.Height; y++)
        {
            for (int x = 0; x < screen.Width; x++)
            {
                int val = (x % 8) * 32; // 0..224
                screen.SetPixel(x, y, Color.FromArgb(val, val, val));
            }
        }
        var screenSource = new SingleBitmapScreenSource(() => new Bitmap(screen));
        var ocr = new CapturingOcr();
        using var provider = new TestLoggerProvider(LogLevel.Debug);
        using var factory = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(provider); });
        var logger = factory.CreateLogger<TextMatchEvaluator>();
        var eval = new TextMatchEvaluator(ocr, screenSource, logger);

        var trig = new ProfileTrigger
        {
            Id = "t3",
            Type = TriggerType.TextMatch,
            Enabled = true,
            Params = new TextMatchParams
            {
                Target = "HELLO",
                Region = new GameBot.Domain.Profiles.Region { X = 0.0, Y = 0.0, Width = 1.0, Height = 1.0 },
                ConfidenceThreshold = 0.10,
                Mode = "found",
                Language = null
            }
        };

        var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
        res.Status.Should().Be(TriggerStatus.Satisfied);

        ocr.LastImageClone.Should().NotBeNull();
        using var img = ocr.LastImageClone!;
        // Sample pixels and ensure they are either black or white after preprocessing
        int samples = 0, bwOk = 0;
        for (int y = 0; y < img.Height; y += Math.Max(1, img.Height / 16))
        {
            for (int x = 0; x < img.Width; x += Math.Max(1, img.Width / 16))
            {
                var c = img.GetPixel(x, y);
                bool isBlack = c.R == 0 && c.G == 0 && c.B == 0;
                bool isWhite = c.R == 255 && c.G == 255 && c.B == 255;
                if (isBlack || isWhite) bwOk++;
                samples++;
            }
        }
        bwOk.Should().Be(samples); // all samples should be strictly black/white
    }
}
