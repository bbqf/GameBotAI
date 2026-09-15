using System;
using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Composite conditions must be validated in <em>every</em> position that accepts a condition
/// (feature 088, FR-005), not only the obvious ones.
/// <para>
/// The while and repeat-until positions matter most here. Before this feature the validation service
/// never inspected them at all — only a count loop's <c>Count</c> — so a malformed composite would
/// have passed save and failed at run time, which FR-010 forbids. These tests also pin the boundary
/// that keeps that addition safe: a <em>leaf</em> in those positions is still not newly validated,
/// because doing so would reject stored sequences that save today (research decision D-006).
/// </para>
/// </summary>
public sealed class CompositeConditionPositionValidationTests {
  private static readonly SequenceStepValidationService Svc = new();

  /// <summary>A composite that is invalid for the simplest possible reason: it combines nothing.</summary>
  private static SequenceStepCondition EmptyComposite() => new AllStepCondition();

  /// <summary>A leaf that would be invalid if the position validated leaves.</summary>
  private static SequenceStepCondition BadLeaf() => new ImageVisibleStepCondition { ImageId = "" };

  private static SequenceStep ActionStep(string stepId, int order = 0) => new() {
    Order = order,
    StepId = stepId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = "tap" }
  };

  private static SequenceStep LoopStep(LoopConfig loop, IReadOnlyList<SequenceStep>? body = null) => new() {
    Order = 0,
    StepId = "loop",
    StepType = SequenceStepType.Loop,
    Loop = loop,
    Body = body ?? new[] { ActionStep("body-step") }
  };

  private static string Joined(IEnumerable<string> errors) => string.Join(" | ", errors);

  // ---------- while / repeat-until: newly validated for composites ----------

  [Fact]
  public void AnInvalidCompositeInAWhileConditionIsRejectedAtSaveTime() {
    var errors = Svc.Validate(new List<SequenceStep> {
      LoopStep(new WhileLoopConfig { Condition = EmptyComposite() })
    });

    Joined(errors).Should().Contain("'all' requires at least one child");
  }

  [Fact]
  public void AnInvalidCompositeInARepeatUntilConditionIsRejectedAtSaveTime() {
    var errors = Svc.Validate(new List<SequenceStep> {
      LoopStep(new RepeatUntilLoopConfig { Condition = EmptyComposite() })
    });

    Joined(errors).Should().Contain("'all' requires at least one child");
  }

  [Fact]
  public void AValidCompositeInAWhileConditionIsAccepted() {
    var errors = Svc.Validate(new List<SequenceStep> {
      LoopStep(new WhileLoopConfig {
        Condition = new AllStepCondition {
          Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "a" } }
        }
      })
    });

    errors.Should().BeEmpty();
  }

  [Theory]
  [InlineData("while")]
  [InlineData("repeatUntil")]
  public void ALeafInALoopConditionIsStillNotNewlyValidated(string loopType) {
    // The D-006 boundary. Stored sequences have never had leaf conditions checked in these two
    // positions; newly rejecting them would break sequences that save today, in an unrelated feature.
    LoopConfig loop = loopType == "while"
      ? new WhileLoopConfig { Condition = BadLeaf() }
      : new RepeatUntilLoopConfig { Condition = BadLeaf() };

    var errors = Svc.Validate(new List<SequenceStep> { LoopStep(loop) });

    errors.Should().BeEmpty();
  }

  // ---------- step guard ----------

  [Fact]
  public void AnInvalidCompositeInAStepGuardIsRejected() {
    var step = ActionStep("s");
    step.Condition = EmptyComposite();

    Joined(Svc.Validate(new List<SequenceStep> { step })).Should().Contain("'all' requires at least one child");
  }

  // ---------- break condition, in a loop body and in an if branch ----------

  [Fact]
  public void AnInvalidCompositeInALoopBodyBreakConditionIsRejected() {
    var breakStep = new SequenceStep {
      Order = 0,
      StepId = "brk",
      StepType = SequenceStepType.Break,
      BreakCondition = EmptyComposite()
    };

    var errors = Svc.Validate(new List<SequenceStep> {
      LoopStep(new CountLoopConfig { Count = 1 }, new[] { breakStep })
    });

    Joined(errors).Should().Contain("'all' requires at least one child");
  }

  [Fact]
  public void AnInvalidCompositeInAnIfBranchBreakConditionIsRejected() {
    var breakStep = new SequenceStep {
      Order = 0,
      StepId = "brk",
      StepType = SequenceStepType.Break,
      BreakCondition = EmptyComposite()
    };

    var ifStep = new SequenceStep {
      Order = 0,
      StepId = "branch",
      StepType = SequenceStepType.If,
      If = new IfConfig { Condition = new ImageVisibleStepCondition { ImageId = "a" } },
      Body = new[] { breakStep }
    };

    var errors = Svc.Validate(new List<SequenceStep> {
      LoopStep(new CountLoopConfig { Count = 1 }, new[] { ifStep })
    });

    Joined(errors).Should().Contain("'all' requires at least one child");
  }

  // ---------- if condition ----------

  [Fact]
  public void AnInvalidCompositeInAnIfConditionIsRejected() {
    var ifStep = new SequenceStep {
      Order = 0,
      StepId = "branch",
      StepType = SequenceStepType.If,
      If = new IfConfig { Condition = EmptyComposite() },
      Body = new[] { ActionStep("then-step") }
    };

    Joined(Svc.Validate(new List<SequenceStep> { ifStep })).Should().Contain("'all' requires at least one child");
  }

  [Fact]
  public void AValidCompositeInAnIfConditionIsAccepted() {
    var ifStep = new SequenceStep {
      Order = 0,
      StepId = "branch",
      StepType = SequenceStepType.If,
      If = new IfConfig {
        Condition = new AllStepCondition {
          Children = new SequenceStepCondition[] {
            new ImageVisibleStepCondition { ImageId = "confirm" },
            new ImageVisibleStepCondition { ImageId = "gas-title", Negate = true }
          }
        }
      },
      Body = new[] { ActionStep("then-step") }
    };

    Svc.Validate(new List<SequenceStep> { ifStep }).Should().BeEmpty();
  }

  // ---------- existing leaf messages are unchanged ----------

  [Fact]
  public void ALeafStepGuardStillProducesItsOriginalMessageExactlyOnce() {
    var step = ActionStep("s");
    step.Condition = BadLeaf();

    var errors = Svc.Validate(new List<SequenceStep> { step });

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 's' imageVisible condition requires imageId.");
  }
}
