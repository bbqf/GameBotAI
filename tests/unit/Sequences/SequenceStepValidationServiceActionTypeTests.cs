using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 102 (issue #201): an unsupported or missing <c>primitiveAction.type</c> is rejected with a message that
/// lists the supported values, the same way the reschedule-self option error lists its own, so a caller never has to
/// guess names. Every listed value must still be accepted, case-insensitively.
/// </summary>
public sealed class SequenceStepValidationServiceActionTypeTests {
  private const string NotSupported = "is not a supported primitive action type";

  private static readonly SequenceStepValidationService Svc = new();

  private static SequenceStep ActionStep(string stepId, string type, Dictionary<string, object?>? payload = null) {
    var action = new SequenceActionPayload { Type = type };
    if (payload is not null) {
      foreach (var kv in payload) action.Parameters[kv.Key] = kv.Value;
    }
    return new SequenceStep {
      Order = 0,
      StepId = stepId,
      StepType = SequenceStepType.Action,
      Action = action
    };
  }

  private static string ExpectedError(string stepId, string type)
    => $"Step '{stepId}' action type '{type}' {NotSupported} (expected one of {SequenceActionTypes.SupportedValuesText}).";

  [Fact]
  public void UnknownActionTypeErrorListsSupportedValues() {
    var errors = Svc.Validate(new[] { ActionStep("a", "bogus") });

    errors.Should().ContainSingle().Which.Should().Be(ExpectedError("a", "bogus"));
    foreach (var type in SequenceActionTypes.All) {
      errors[0].Should().Contain(type);
    }
  }

  [Fact]
  public void BlankActionTypeErrorListsSupportedValues() {
    var errors = Svc.Validate(new[] { ActionStep("a", string.Empty) });

    errors.Should().ContainSingle().Which.Should().Be(ExpectedError("a", string.Empty));
  }

  public static TheoryData<string> SupportedTypes() {
    var data = new TheoryData<string>();
    foreach (var type in SequenceActionTypes.All) data.Add(type);
    return data;
  }

  [Theory]
  [MemberData(nameof(SupportedTypes))]
  public void EveryListedTypeIsAccepted(string type) {
    var errors = Svc.Validate(new[] { ActionStep("a", type, MinimalPayload(type)) });

    errors.Should().NotContain(e => e.Contains(NotSupported, StringComparison.Ordinal));
  }

  [Theory]
  [InlineData("TAP")]
  [InlineData("waitforimage")]
  public void MatchingIsCaseInsensitive(string type) {
    var errors = Svc.Validate(new[] { ActionStep("a", type) });

    errors.Should().NotContain(e => e.Contains(NotSupported, StringComparison.Ordinal));
  }

  [Fact]
  public void SupportedValuesTextJoinsAllInOrder() {
    SequenceActionTypes.SupportedValuesText.Split(", ").Should().Equal(SequenceActionTypes.All);
    SequenceActionTypes.All.Should().HaveCount(11)
      .And.Contain(new[] { ActionTypes.RescheduleSelf, ActionTypes.Notify });
    SequenceActionTypes.All.Take(PrimitiveActionTypes.All.Count).Should().Equal(PrimitiveActionTypes.All);
  }

  private static Dictionary<string, object?>? MinimalPayload(string type) => type switch {
    ActionTypes.Command => new() { ["commandId"] = "cmd-1" },
    ActionTypes.RescheduleSelf => new() { ["option"] = "OncePerRun" },
    ActionTypes.Notify => new() { ["message"] = "hello" },
    _ => null
  };
}
