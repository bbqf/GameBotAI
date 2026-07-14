using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 068: cross-field validation of the ocrOffset spec on reschedule-self.</summary>
public sealed class OcrOffsetValidationTests {
  private static readonly SequenceStepValidationService Validator = new();

  private static Dictionary<string, object?> Region(int x, int y, int w, int h) =>
    new() { ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h };

  private static IReadOnlyList<string> ValidateReschedule(string option, Dictionary<string, object?> ocrOffset) {
    var step = new SequenceStep {
      Order = 0,
      StepId = "reschedule",
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload {
        Type = ActionTypes.RescheduleSelf,
        Parameters = { ["option"] = option, ["ocrOffset"] = ocrOffset }
      }
    };
    return Validator.Validate(new[] { step });
  }

  [Fact]
  public void ValidTimerOcrOffsetPasses() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(10, 20, 120, 40),
      ["fallback"] = "00:06:00"
    };
    ValidateReschedule("Timer", ocr).Should().BeEmpty();
  }

  [Fact]
  public void OcrOffsetOnNonTimerOptionIsRejected() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(10, 20, 120, 40),
      ["fallback"] = "00:06:00"
    };
    ValidateReschedule("OncePerRun", ocr)
      .Should().ContainSingle(e => e.Contains("ocrOffset is only valid when option is Timer"));
  }

  [Fact]
  public void NonPositiveRegionIsRejected() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(10, 20, 0, 40),
      ["fallback"] = "00:06:00"
    };
    ValidateReschedule("Timer", ocr)
      .Should().Contain(e => e.Contains("positive width and height"));
  }

  [Fact]
  public void MinNotLessThanMaxIsRejected() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(10, 20, 120, 40),
      ["fallback"] = "00:06:00",
      ["min"] = "01:00:00",
      ["max"] = "00:30:00"
    };
    ValidateReschedule("Timer", ocr)
      .Should().Contain(e => e.Contains("min must be less than ocrOffset.max"));
  }

  [Fact]
  public void MissingFallbackIsRejected() {
    var ocr = new Dictionary<string, object?> { ["region"] = Region(10, 20, 120, 40) };
    ValidateReschedule("Timer", ocr)
      .Should().Contain(e => e.Contains("fallback"));
  }

  [Fact]
  public void TimerWithOcrOffsetDoesNotRequireStaticTimerFields() {
    // Regression guard: the static "Timer requires timerTimeOfDay or timerRelativeOffset" rule
    // must not fire when ocrOffset supplies the offset.
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(10, 20, 120, 40),
      ["fallback"] = "00:06:00"
    };
    ValidateReschedule("Timer", ocr).Should().NotContain(e => e.Contains("requires a timerTimeOfDay"));
  }
}
