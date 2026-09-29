using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Issue #228: a reschedule-self payload with an unknown top-level field must get an error that
/// names the field. The run-time reader still ignores unknown keys.
/// </summary>
public sealed class RescheduleSelfUnknownFieldValidationTests {
  private static readonly SequenceStepValidationService Validator = new();

  private static SequenceActionPayload Payload(params (string Key, object? Value)[] pairs) {
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    foreach (var (key, value) in pairs) {
      action.Parameters[key] = value;
    }
    return action;
  }

  private static IReadOnlyList<string> Validate(SequenceActionPayload action) {
    var step = new SequenceStep {
      Order = 0,
      StepId = "reschedule",
      StepType = SequenceStepType.Action,
      Action = action
    };
    return Validator.Validate(new[] { step });
  }

  private static Dictionary<string, object?> ValidOcr() => new() {
    ["region"] = new Dictionary<string, object?> { ["x"] = 10, ["y"] = 20, ["width"] = 120, ["height"] = 40 },
    ["fallback"] = "00:06:00"
  };

  // ── US1: unknown field is rejected ────────────────────────────────────────

  [Fact]
  public void UnknownFieldOnValidTimerPayloadIsRejectedAndNamed() {
    var errors = Validate(Payload(("option", "Timer"), ("timerTimeOfDay", "11:00"), ("nextDay", true)));
    var error = errors.Should().ContainSingle().Subject;
    error.Should().Contain("unknown field(s): nextDay");
    error.Should().Contain("Known fields: option, timerTimeOfDay, timerRelativeOffset, ocrOffset.");
  }

  [Fact]
  public void TwoUnknownFieldsGiveOneErrorThatNamesBoth() {
    var errors = Validate(Payload(("option", "OncePerRun"), ("foo", 1), ("bar", 2)));
    var error = errors.Should().ContainSingle().Subject;
    error.Should().Contain("foo");
    error.Should().Contain("bar");
  }

  [Fact]
  public void KeysThatDifferOnlyInCaseAreBothListedAsWritten() {
    var errors = Validate(Payload(("option", "OncePerRun"), ("nextDay", true), ("NextDay", true)));
    var error = errors.Should().ContainSingle().Subject;
    error.Should().Contain("nextDay");
    error.Should().Contain("NextDay");
  }

  // ── US2: valid payloads stay valid ────────────────────────────────────────

  [Fact]
  public void TimerWithTimeOfDayStaysValid() =>
    Validate(Payload(("option", "Timer"), ("timerTimeOfDay", "11:00"))).Should().BeEmpty();

  [Fact]
  public void TimerWithRelativeOffsetStaysValid() =>
    Validate(Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00"))).Should().BeEmpty();

  [Fact]
  public void TimerWithOcrOffsetStaysValid() =>
    Validate(Payload(("option", "Timer"), ("ocrOffset", ValidOcr()))).Should().BeEmpty();

  [Theory]
  [InlineData("OncePerRun")]
  [InlineData("AtQueueStart")]
  [InlineData("EveryStep")]
  public void NonTimerOptionsStayValid(string option) =>
    Validate(Payload(("option", option))).Should().BeEmpty();

  [Fact]
  public void KnownFieldWithOtherLetterCaseStaysValid() =>
    Validate(Payload(("option", "Timer"), ("TimerTimeOfDay", "11:00"))).Should().BeEmpty();

  [Fact]
  public void UnknownFieldInsideOcrOffsetIsNotRejected() {
    var ocr = ValidOcr();
    ocr["nextDay"] = true;
    Validate(Payload(("option", "Timer"), ("ocrOffset", ocr))).Should().BeEmpty();
  }

  [Fact]
  public void RuntimeReaderStillAcceptsAnUnknownKey() {
    var ok = SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerTimeOfDay", "11:00"), ("nextDay", true)), out var result, out var error);
    ok.Should().BeTrue();
    error.Should().BeNull();
    result.Should().NotBeNull();
  }

  // ── US3: the option check does not change ─────────────────────────────────

  [Fact]
  public void BogusOptionKeepsItsMessageAndGivesNoUnknownFieldError() {
    var errors = Validate(Payload(("option", "Bogus")));
    errors.Should().ContainSingle()
      .Which.Should().Contain("option 'Bogus' is not a known schedule option (expected one of AtQueueStart, OncePerRun, Timer, EveryStep)");
    errors.Should().NotContain(e => e.Contains("unknown field"));
  }

  [Fact]
  public void BogusOptionWithUnknownFieldGivesOnlyTheOptionError() {
    var errors = Validate(Payload(("option", "Bogus"), ("nextDay", true)));
    errors.Should().ContainSingle()
      .Which.Should().Contain("is not a known schedule option");
    errors.Should().NotContain(e => e.Contains("unknown field"));
  }
}
