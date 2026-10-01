using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 123: the validator rules for the reschedule-self option Cancel.</summary>
public sealed class RescheduleSelfCancelValidationTests {
  private static readonly SequenceStepValidationService Validator = new();

  private static IReadOnlyList<string> Validate(params (string Key, object? Value)[] pairs) {
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    foreach (var (key, value) in pairs) {
      action.Parameters[key] = value;
    }
    var step = new SequenceStep { Order = 0, StepId = "cancel", StepType = SequenceStepType.Action, Action = action };
    return Validator.Validate(new[] { step });
  }

  private static Dictionary<string, object?> ValidOcr() => new() {
    ["region"] = new Dictionary<string, object?> { ["x"] = 10, ["y"] = 20, ["width"] = 120, ["height"] = 40 },
    ["fallback"] = "00:06:00"
  };

  [Fact]
  public void ValidCancelPayloadPasses() {
    Validate(("option", "Cancel")).Should().BeEmpty();
  }

  [Fact]
  public void CancelWithTimerTimeOfDayIsRejectedAndNamed() {
    var errors = Validate(("option", "Cancel"), ("timerTimeOfDay", "11:00"));
    errors.Should().ContainSingle().Which.Should().Contain("timerTimeOfDay").And.Contain("only valid when option is Timer");
  }

  [Fact]
  public void CancelWithTimerRelativeOffsetIsRejectedAndNamed() {
    var errors = Validate(("option", "Cancel"), ("timerRelativeOffset", "00:10:00"));
    errors.Should().ContainSingle().Which.Should().Contain("timerRelativeOffset").And.Contain("only valid when option is Timer");
  }

  [Fact]
  public void CancelWithOcrOffsetIsRejectedAndNamed() {
    var errors = Validate(("option", "Cancel"), ("ocrOffset", ValidOcr()));
    errors.Should().ContainSingle().Which.Should().Contain("ocrOffset").And.Contain("only valid when option is Timer");
  }

  [Fact]
  public void CancelWithTwoTimerFieldsReportsBoth() {
    var errors = Validate(("option", "Cancel"), ("timerTimeOfDay", "11:00"), ("timerRelativeOffset", "00:10:00"));
    errors.Should().HaveCount(2);
    errors.Any(e => e.Contains("timerTimeOfDay", System.StringComparison.Ordinal)).Should().BeTrue();
    errors.Any(e => e.Contains("timerRelativeOffset", System.StringComparison.Ordinal)).Should().BeTrue();
  }

  [Fact]
  public void UnknownOptionMessageListsCancel() {
    var errors = Validate(("option", "Bogus"));
    errors.Should().ContainSingle().Which.Should().Contain("Cancel");
  }
}
