using System;
using System.Drawing;
using Xunit;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Tests.Unit.Triggers;

namespace GameBot.Tests.Unit.Triggers {
  public class TextMatchEvaluatorBoundaryTests {
    [Fact]
    public void SatisfiedWhenTextFoundAndConfidenceAtThreshold() {
      var bmp = new Bitmap(8, 8); // dummy image
      var ocr = new StubTextOcr(_ => new OcrResult("target", 0.8));
      var screen = new StubScreenSource(bmp);
      var eval = new TextMatchEvaluator(ocr, screen);
      var trigger = new Trigger {
        Id = "t1",
        Enabled = true,
        Type = TriggerType.TextMatch,
        Params = new TextMatchParams {
          Target = "target",
          ConfidenceThreshold = 0.8,
          Mode = "found",
          Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
        }
      };
      var result = eval.Evaluate(trigger, DateTimeOffset.UtcNow);
      Assert.Equal(TriggerStatus.Satisfied, result.Status);
      Assert.Equal("text_found", result.Reason);
    }

    [Fact]
    public void PendingWhenTextFoundAndConfidenceJustBelowThreshold() {
      var bmp = new Bitmap(8, 8);
      var ocr = new StubTextOcr(_ => new OcrResult("target", 0.7999));
      var screen = new StubScreenSource(bmp);
      var eval = new TextMatchEvaluator(ocr, screen);
      var trigger = new Trigger {
        Id = "t2",
        Enabled = true,
        Type = TriggerType.TextMatch,
        Params = new TextMatchParams {
          Target = "target",
          ConfidenceThreshold = 0.8,
          Mode = "found",
          Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
        }
      };
      var result = eval.Evaluate(trigger, DateTimeOffset.UtcNow);
      Assert.Equal(TriggerStatus.Pending, result.Status);
      Assert.Equal("text_not_found", result.Reason);
    }

    [Fact]
    public void SatisfiedWhenTextNotFoundModeNotFound() {
      var bmp = new Bitmap(8, 8);
      var ocr = new StubTextOcr(_ => new OcrResult("other", 0.9));
      var screen = new StubScreenSource(bmp);
      var eval = new TextMatchEvaluator(ocr, screen);
      var trigger = new Trigger {
        Id = "t3",
        Enabled = true,
        Type = TriggerType.TextMatch,
        Params = new TextMatchParams {
          Target = "target",
          ConfidenceThreshold = 0.8,
          Mode = "not-found",
          Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
        }
      };
      var result = eval.Evaluate(trigger, DateTimeOffset.UtcNow);
      Assert.Equal(TriggerStatus.Satisfied, result.Status);
      Assert.Equal("text_absent", result.Reason);
    }

    [Fact]
    public void PendingWhenTextPresentModeNotFound() {
      var bmp = new Bitmap(8, 8);
      var ocr = new StubTextOcr(_ => new OcrResult("target", 0.9));
      var screen = new StubScreenSource(bmp);
      var eval = new TextMatchEvaluator(ocr, screen);
      var trigger = new Trigger {
        Id = "t4",
        Enabled = true,
        Type = TriggerType.TextMatch,
        Params = new TextMatchParams {
          Target = "target",
          ConfidenceThreshold = 0.8,
          Mode = "not-found",
          Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
        }
      };
      var result = eval.Evaluate(trigger, DateTimeOffset.UtcNow);
      Assert.Equal(TriggerStatus.Pending, result.Status);
      Assert.Equal("text_present", result.Reason);
    }
  }
}
