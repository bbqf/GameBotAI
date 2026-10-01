using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 125: validation of the <c>keep</c> key of a <c>reschedule-self</c> payload.</summary>
public sealed class RescheduleSelfKeepValidationTests {
  private static readonly SequenceStepValidationService Validator = new();

  private static IReadOnlyList<string> Validate(params (string Key, object? Value)[] pairs) {
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    foreach (var (key, value) in pairs) {
      action.Parameters[key] = value;
    }
    var step = new SequenceStep { Order = 0, StepId = "reschedule", StepType = SequenceStepType.Action, Action = action };
    return Validator.Validate(new[] { step });
  }

  [Theory]
  [InlineData("earliest")]
  [InlineData("Earliest")]
  public void TimerWithKeepEarliestIsValid(string keep) =>
    Validate(("option", "Timer"), ("timerRelativeOffset", "00:10:00"), ("keep", keep)).Should().BeEmpty();

  [Fact]
  public void TimerWithTimeOfDayAndKeepIsValid() =>
    Validate(("option", "Timer"), ("timerTimeOfDay", "11:00"), ("keep", "earliest")).Should().BeEmpty();

  [Fact]
  public void AnInvalidKeepValueIsRejectedWithAMessageThatNamesEarliest() {
    var errors = Validate(("option", "Timer"), ("timerRelativeOffset", "00:10:00"), ("keep", "latest"));

    errors.Should().ContainSingle().Which.Should().Contain("earliest").And.Contain("latest");
    errors.Should().NotContain(e => e.Contains("unknown field"));
  }

  [Theory]
  [InlineData("OncePerRun")]
  [InlineData("AtQueueStart")]
  [InlineData("EveryStep")]
  [InlineData("Cancel")]
  public void KeepWithAnOptionOtherThanTimerIsRejected(string option) {
    var errors = Validate(("option", option), ("keep", "earliest"));

    errors.Should().ContainSingle().Which.Should().Contain("keep is only valid when option is Timer");
  }

  [Fact]
  public void KeyThatDiffersOnlyInCaseIsAccepted() =>
    Validate(("option", "Timer"), ("timerRelativeOffset", "00:10:00"), ("Keep", "earliest")).Should().BeEmpty();
}
