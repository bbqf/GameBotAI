using System;
using System.Drawing;
using FluentAssertions;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.UnitTests;

public class TextMatchEvaluatorCropTests {
  [Fact]
  public void CropUsesPixelCoordinatesWhenRegionValuesExceedOne() {
    using var screen = new Bitmap(100, 50);
    var screenSrc = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
    var ocr = new CapturingOcr("ok", 0.99);
    var eval = new TextMatchEvaluator(ocr, screenSrc);

    var trig = new Trigger {
      Id = "t-pixel",
      Type = TriggerType.TextMatch,
      Enabled = true,
      CooldownSeconds = 0,
      Params = new TextMatchParams {
        Target = "irrelevant",
        // Intentionally pass pixel coordinates
        Region = new GameBot.Domain.Triggers.Region { X = 10, Y = 5, Width = 20, Height = 10 },
        ConfidenceThreshold = 0.5,
        Mode = "found"
      }
    };

    var now = DateTimeOffset.UtcNow;
    _ = eval.Evaluate(trig, now);

    // Preprocessing upscales to at least height 32; 10 -> 32 (scale 3.2), width 20 -> 64
    ocr.LastWidth.Should().Be(64, "preprocessed width should reflect scaled pixel-region width");
    ocr.LastHeight.Should().Be(32, "preprocessed height should reflect MinHeight scaling");
  }

  [Fact]
  public void CropUsesNormalizedCoordinatesWhenRegionValuesWithinUnit() {
    using var screen = new Bitmap(200, 100);
    var screenSrc = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
    var ocr = new CapturingOcr("ok", 0.99);
    var eval = new TextMatchEvaluator(ocr, screenSrc);

    var trig = new Trigger {
      Id = "t-norm",
      Type = TriggerType.TextMatch,
      Enabled = true,
      CooldownSeconds = 0,
      Params = new TextMatchParams {
        Target = "irrelevant",
        Region = new GameBot.Domain.Triggers.Region { X = 0.25, Y = 0.1, Width = 0.5, Height = 0.2 },
        ConfidenceThreshold = 0.5,
        Mode = "found"
      }
    };

    var now = DateTimeOffset.UtcNow;
    _ = eval.Evaluate(trig, now);

    // 0.5 * 200 = 100, 0.2 * 100 = 20 before preprocessing
    // Preprocessing scales height 20 by 2.0 => 40; width 100 by 2.0 => 200
    ocr.LastWidth.Should().Be(200);
    ocr.LastHeight.Should().Be(40);
  }

  private sealed class CapturingOcr : ITextOcr {
    private readonly string _text; private readonly double _conf;
    public int LastWidth { get; private set; }
    public int LastHeight { get; private set; }
    public CapturingOcr(string text, double conf) { _text = text; _conf = conf; }
    public OcrResult Recognize(Bitmap image) {
      LastWidth = image.Width; LastHeight = image.Height; return new OcrResult(_text, _conf);
    }
    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
  }
}
