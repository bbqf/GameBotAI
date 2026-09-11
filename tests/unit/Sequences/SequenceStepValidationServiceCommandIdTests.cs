using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Unit tests for bug B-003: a "command" step must carry a resolved commandId. A bare
/// commandName (or no reference at all) used to be silently accepted and defaulted the
/// dispatch target to the step's own stepId, failing confusingly at run time instead of here.
/// </summary>
public sealed class SequenceStepValidationServiceCommandIdTests {
  private static readonly SequenceStepValidationService Svc = new();

  private static SequenceStep CommandStep(string stepId, Dictionary<string, object?>? payload = null) {
    var action = new SequenceActionPayload { Type = "command" };
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

  [Fact]
  public void TopLevelCommandStepWithCommandNameButNoCommandIdIsRejected() {
    var step = CommandStep("cmd1", new Dictionary<string, object?> { ["commandName"] = "some-command" });

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("cmd1") && e.Contains("commandId"));
  }

  [Fact]
  public void TopLevelCommandStepWithNoPayloadAtAllIsRejected() {
    var step = CommandStep("cmd1");

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("cmd1") && e.Contains("commandId"));
  }

  [Fact]
  public void CommandStepNestedInsideLoopBodyWithoutCommandIdIsRejected() {
    var nested = CommandStep("cmd1", new Dictionary<string, object?> { ["commandName"] = "some-command" });
    var loop = new SequenceStep {
      Order = 0,
      StepId = "loop1",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 1 },
      Body = new List<SequenceStep> { nested }
    };

    var errors = Svc.Validate(new[] { loop });

    errors.Should().ContainSingle(e => e.Contains("cmd1") && e.Contains("commandId"));
  }

  [Fact]
  public void CommandStepNestedInsideIfBranchWithoutCommandIdIsRejected() {
    var nested = CommandStep("cmd1", new Dictionary<string, object?> { ["commandName"] = "some-command" });
    var ifStep = new SequenceStep {
      Order = 0,
      StepId = "if1",
      StepType = SequenceStepType.If,
      If = new IfConfig { Condition = new ImageVisibleStepCondition { ImageId = "img" } },
      Body = new List<SequenceStep> { nested }
    };

    var errors = Svc.Validate(new[] { ifStep });

    errors.Should().ContainSingle(e => e.Contains("cmd1") && e.Contains("commandId"));
  }

  [Fact]
  public void CommandStepWithValidCommandIdIsAccepted() {
    var step = CommandStep("cmd1", new Dictionary<string, object?> { ["commandId"] = "real-command-id" });

    var errors = Svc.Validate(new[] { step });

    errors.Should().BeEmpty();
  }

  [Fact]
  public void CommandStepWithBothCommandNameAndCommandIdIsAccepted() {
    var step = CommandStep("cmd1", new Dictionary<string, object?> {
      ["commandName"] = "some-command",
      ["commandId"] = "real-command-id"
    });

    var errors = Svc.Validate(new[] { step });

    errors.Should().BeEmpty();
  }
}
