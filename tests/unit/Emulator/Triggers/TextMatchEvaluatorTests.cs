using System;
using System.Drawing;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.UnitTests;

public class TextMatchEvaluatorTests
{
    [Fact]
    public void FoundModeSatisfiedWhenTextPresentAndConfident()
    {
        var ocr = new FakeOcr("Hello World", 0.95);
        using var bmp = new Bitmap(2,2);
        var screen = new SingleBitmapScreenSource(() => (Bitmap)bmp.Clone());
        var eval = new TextMatchEvaluator(ocr, screen);

        var trig = new Trigger
        {
            Id = "t1",
            Type = TriggerType.TextMatch,
            Enabled = true,
            CooldownSeconds = 0,
            Params = new TextMatchParams
            {
                Target = "world",
                Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 },
                ConfidenceThreshold = 0.90,
                Mode = "found"
            }
        };

        var now = DateTimeOffset.UtcNow;
        var res = eval.Evaluate(trig, now);
        res.Status.Should().Be(TriggerStatus.Satisfied);
    }

    [Fact]
    public void NotFoundModeSatisfiedWhenTextAbsentOrLowConfidence()
    {
        var ocr = new FakeOcr("Something else", 0.99);
        using var bmp = new Bitmap(2,2);
        var screen = new SingleBitmapScreenSource(() => (Bitmap)bmp.Clone());
        var eval = new TextMatchEvaluator(ocr, screen);

        var trig = new Trigger
        {
            Id = "t2",
            Type = TriggerType.TextMatch,
            Enabled = true,
            CooldownSeconds = 0,
            Params = new TextMatchParams
            {
                Target = "world",
                Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 },
                ConfidenceThreshold = 0.80,
                Mode = "not-found"
            }
        };

        var now = DateTimeOffset.UtcNow;
        var res = eval.Evaluate(trig, now);
        res.Status.Should().Be(TriggerStatus.Satisfied);
    }

    private sealed class FakeOcr : ITextOcr
    {
        private readonly string _text; private readonly double _conf;
        public FakeOcr(string text, double conf) { _text = text; _conf = conf; }
        public OcrResult Recognize(Bitmap image) => new OcrResult(_text, _conf);
        public OcrResult Recognize(Bitmap image, string? language) => new OcrResult(_text, _conf);
    }
}
