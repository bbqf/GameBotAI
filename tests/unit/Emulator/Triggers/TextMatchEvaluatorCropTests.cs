using System;
using System.Drawing;
using FluentAssertions;
using GameBot.Domain.Profiles;
using GameBot.Domain.Profiles.Evaluators;
using Xunit;

namespace GameBot.UnitTests;

public class TextMatchEvaluatorCropTests
{
    [Fact]
    public void CropUsesPixelCoordinatesWhenRegionValuesExceedOne()
    {
        using var screen = new Bitmap(100, 50);
        var screenSrc = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
        var ocr = new CapturingOcr("ok", 0.99);
        var eval = new TextMatchEvaluator(ocr, screenSrc);

        var trig = new ProfileTrigger
        {
            Id = "t-pixel",
            Type = TriggerType.TextMatch,
            Enabled = true,
            CooldownSeconds = 0,
            Params = new TextMatchParams
            {
                Target = "irrelevant",
                // Intentionally pass pixel coordinates
                Region = new GameBot.Domain.Profiles.Region { X = 10, Y = 5, Width = 20, Height = 10 },
                ConfidenceThreshold = 0.5,
                Mode = "found"
            }
        };

        var now = DateTimeOffset.UtcNow;
        _ = eval.Evaluate(trig, now);

        ocr.LastWidth.Should().Be(20, "pixel-region width should be used for cropping");
        ocr.LastHeight.Should().Be(10, "pixel-region height should be used for cropping");
    }

    [Fact]
    public void CropUsesNormalizedCoordinatesWhenRegionValuesWithinUnit()
    {
        using var screen = new Bitmap(200, 100);
        var screenSrc = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
        var ocr = new CapturingOcr("ok", 0.99);
        var eval = new TextMatchEvaluator(ocr, screenSrc);

        var trig = new ProfileTrigger
        {
            Id = "t-norm",
            Type = TriggerType.TextMatch,
            Enabled = true,
            CooldownSeconds = 0,
            Params = new TextMatchParams
            {
                Target = "irrelevant",
                Region = new GameBot.Domain.Profiles.Region { X = 0.25, Y = 0.1, Width = 0.5, Height = 0.2 },
                ConfidenceThreshold = 0.5,
                Mode = "found"
            }
        };

        var now = DateTimeOffset.UtcNow;
        _ = eval.Evaluate(trig, now);

        // 0.5 * 200 = 100, 0.2 * 100 = 20
        ocr.LastWidth.Should().Be(100);
        ocr.LastHeight.Should().Be(20);
    }

    private sealed class CapturingOcr : ITextOcr
    {
        private readonly string _text; private readonly double _conf;
        public int LastWidth { get; private set; }
        public int LastHeight { get; private set; }
        public CapturingOcr(string text, double conf) { _text = text; _conf = conf; }
        public OcrResult Recognize(Bitmap image)
        {
            LastWidth = image.Width; LastHeight = image.Height; return new OcrResult(_text, _conf);
        }
        public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
    }
}
