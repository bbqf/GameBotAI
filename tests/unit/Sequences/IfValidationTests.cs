using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Unit tests for if-step validation rules (feature 067): required if configuration, flat
/// branches (no loops, no nested ifs), break placement, duplicate branch ids, condition
/// field requirements, and commandOutcome scoping within a branch.
/// </summary>
public sealed class IfValidationTests {
  private static readonly SequenceStepValidationService Svc = new();

  private static SequenceStep ActionStep(string stepId, int order = 0)
      => new() {
        Order = order,
        StepId = stepId,
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = "tap" }
      };

  private static SequenceStep IfStep(
      string stepId,
      IReadOnlyList<SequenceStep>? thenBranch = null,
      IReadOnlyList<SequenceStep>? elseBranch = null,
      SequenceStepCondition? condition = null)
      => new() {
        Order = 0,
        StepId = stepId,
        StepType = SequenceStepType.If,
        If = new IfConfig { Condition = condition ?? new ImageVisibleStepCondition { ImageId = "img" } },
        Body = thenBranch ?? Array.Empty<SequenceStep>(),
        ElseBody = elseBranch
      };

  [Fact]
  public void IfStepWithoutConfigIsRejected() {
    var step = new SequenceStep {
      Order = 0,
      StepId = "if1",
      StepType = SequenceStepType.If,
      Body = new List<SequenceStep> { ActionStep("t1") }
    };

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("requires an if configuration"));
  }

  [Fact]
  public void IfStepWithEmptyBranchesIsAccepted() {
    var errors = Svc.Validate(new[] { IfStep("if1") });
    errors.Should().BeEmpty();
  }

  [Fact]
  public void IfStepWithPopulatedBranchesIsAccepted() {
    var step = IfStep("if1",
        new List<SequenceStep> { ActionStep("t1") },
        new List<SequenceStep> { ActionStep("e1") });

    var errors = Svc.Validate(new[] { step });

    errors.Should().BeEmpty();
  }

  [Fact]
  public void ImageVisibleConditionWithoutImageIdIsRejected() {
    var step = IfStep("if1", condition: new ImageVisibleStepCondition { ImageId = "" });
    var errors = Svc.Validate(new[] { step });
    errors.Should().ContainSingle(e => e.Contains("imageVisible condition requires imageId"));
  }

  [Fact]
  public void CommandOutcomeConditionWithoutStepRefIsRejected() {
    var step = IfStep("if1", condition: new CommandOutcomeStepCondition { StepRef = "", ExpectedState = "success" });
    var errors = Svc.Validate(new[] { step });
    errors.Should().Contain(e => e.Contains("commandOutcome condition requires stepRef"));
  }

  [Fact]
  public void CommandOutcomeConditionWithUnknownExpectedStateIsRejected() {
    var step = IfStep("if1", condition: new CommandOutcomeStepCondition { StepRef = "prev", ExpectedState = "banana" });
    var errors = Svc.Validate(new[] { step });
    errors.Should().Contain(e => e.Contains("expectedState must be one of success|failed|skipped"));
  }

  [Fact]
  public void LoopInsideThenBranchIsRejected() {
    var nestedLoop = new SequenceStep {
      Order = 0,
      StepId = "loop1",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 1 }
    };
    var step = IfStep("if1", new List<SequenceStep> { nestedLoop });

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("must not itself be a loop step"));
  }

  [Fact]
  public void IfInsideElseBranchIsRejected() {
    var nestedIf = IfStep("if2");
    var step = IfStep("if1", elseBranch: new List<SequenceStep> { nestedIf });

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("must not itself be an if step"));
  }

  [Fact]
  public void BreakInBranchOfTopLevelIfIsRejected() {
    var brk = new SequenceStep { Order = 0, StepId = "brk", StepType = SequenceStepType.Break };
    var step = IfStep("if1", new List<SequenceStep> { brk });

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("only valid inside a loop body"));
  }

  [Fact]
  public void BreakInBranchOfIfInsideLoopIsAccepted() {
    var brk = new SequenceStep { Order = 0, StepId = "brk", StepType = SequenceStepType.Break };
    var nestedIf = IfStep("if1", new List<SequenceStep> { brk });
    var loop = new SequenceStep {
      Order = 0,
      StepId = "loop1",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 3 },
      Body = new List<SequenceStep> { nestedIf }
    };

    var errors = Svc.Validate(new[] { loop });

    errors.Should().BeEmpty();
  }

  [Fact]
  public void DuplicateStepIdsWithinBranchAreRejected() {
    var step = IfStep("if1", new List<SequenceStep> { ActionStep("dup"), ActionStep("dup", 1) });

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("Duplicate branch step id 'dup'"));
  }

  [Fact]
  public void BranchStepWithoutActionPayloadIsRejected() {
    var bare = new SequenceStep { Order = 0, StepId = "t1", StepType = SequenceStepType.Action };
    var step = IfStep("if1", new List<SequenceStep> { bare });

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("requires action payload"));
  }

  [Fact]
  public void CommandOutcomeForwardReferenceWithinBranchIsRejected() {
    var first = ActionStep("t1");
    first.Condition = new CommandOutcomeStepCondition { StepRef = "t2", ExpectedState = "success" };
    var second = ActionStep("t2", 1);
    var step = IfStep("if1", new List<SequenceStep> { first, second });

    var errors = Svc.Validate(new[] { step });

    errors.Should().Contain(e => e.Contains("must reference a prior step"));
  }

  [Fact]
  public void TemplatePlaceholderInTopLevelIfBranchIsRejected() {
    var branchStep = ActionStep("t1");
    branchStep.Action!.Parameters["target"] = "point-{{iteration}}";
    var step = IfStep("if1", new List<SequenceStep> { branchStep });

    var errors = Svc.Validate(new[] { step });

    errors.Should().ContainSingle(e => e.Contains("template placeholder"));
  }
}
