using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 167: validation of the commandStepOutcome condition (messages from the contract).</summary>
public sealed class CommandStepOutcomeValidationTests {
  private static SequenceStep CommandStep(string id, SequenceStepCondition? condition = null) {
    var action = new SequenceActionPayload { Type = "command" };
    action.Parameters["commandId"] = "cmd-" + id;
    return new SequenceStep {
      StepId = id,
      CommandId = "cmd-" + id,
      StepType = SequenceStepType.Action,
      Action = action,
      Condition = condition
    };
  }

  private static CommandStepOutcomeStepCondition Leaf(string stepRef = "send-1", int order = 12, string state = "executed") => new() {
    StepRef = stepRef,
    CommandStepOrder = order,
    ExpectedState = state
  };

  private static IReadOnlyList<string> Validate(params SequenceStep[] steps) {
    for (var i = 0; i < steps.Length; i++) steps[i].Order = i;
    var errors = new SequenceStepValidationService().Validate(steps);
    return errors.Where(e => e.Contains("commandStepOutcome", System.StringComparison.Ordinal)).ToList();
  }

  [Fact]
  public void AValidConditionHasNoError() {
    Validate(CommandStep("send-1"), CommandStep("next", Leaf())).Should().BeEmpty();
  }

  [Fact]
  public void AnEmptyStepRefIsRejected() {
    Validate(CommandStep("send-1"), CommandStep("next", Leaf(stepRef: "")))
      .Should().Contain("Step 'next' commandStepOutcome condition requires stepRef.");
  }

  [Fact]
  public void AnUnknownStepIsRejected() {
    Validate(CommandStep("send-1"), CommandStep("next", Leaf(stepRef: "nope")))
      .Should().Contain("Step 'next' commandStepOutcome references unknown prior step 'nope'.");
  }

  [Fact]
  public void AStepThatIsNotBeforeIsRejected() {
    Validate(CommandStep("next", Leaf(stepRef: "later")), CommandStep("later"))
      .Should().Contain("Step 'next' commandStepOutcome stepRef 'later' must reference a prior step.");
  }

  [Fact]
  public void AStepThatDoesNotRunACommandIsRejected() {
    var tap = new SequenceStep {
      StepId = "tap-1", StepType = SequenceStepType.Action, Action = new SequenceActionPayload { Type = "tap" }
    };

    Validate(tap, CommandStep("next", Leaf(stepRef: "tap-1")))
      .Should().Contain("Step 'next' commandStepOutcome stepRef 'tap-1' must reference a step that runs a command.");
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-4)]
  public void AnOrderBelowOneIsRejected(int order) {
    Validate(CommandStep("send-1"), CommandStep("next", Leaf(order: order)))
      .Should().Contain("Step 'next' commandStepOutcome commandStepOrder must be 1 or more.");
  }

  [Theory]
  [InlineData("")]
  [InlineData("success")]
  [InlineData("detection_failed_after_3_retries")]
  public void ABadStateIsRejected(string state) {
    Validate(CommandStep("send-1"), CommandStep("next", Leaf(state: state)))
      .Should().Contain("Step 'next' commandStepOutcome expectedState must be one of executed|skipped_detection_failed|completed_timeout|image_detected.");
  }

  [Fact]
  public void TheSameChecksApplyToAChildOfAComposite() {
    var all = new AllStepCondition { Children = new SequenceStepCondition[] { Leaf(stepRef: "nope", order: 0, state: "bad") } };

    var errors = Validate(CommandStep("send-1"), CommandStep("next", all));

    errors.Should().Contain(e => e.Contains("$.children[0]", System.StringComparison.Ordinal) && e.Contains("unknown prior step 'nope'", System.StringComparison.Ordinal));
    errors.Should().Contain(e => e.Contains("$.children[0]", System.StringComparison.Ordinal) && e.Contains("commandStepOrder must be 1 or more", System.StringComparison.Ordinal));
    errors.Should().Contain(e => e.Contains("$.children[0]", System.StringComparison.Ordinal) && e.Contains("expectedState must be one of", System.StringComparison.Ordinal));
  }

  [Fact]
  public void ABreakConditionAndAnIfConditionAreChecked() {
    var loop = new SequenceStep {
      StepId = "loop", StepType = SequenceStepType.Loop, Loop = new CountLoopConfig { Count = 2 },
      Body = new[] { new SequenceStep { StepId = "stop", StepType = SequenceStepType.Break, BreakCondition = Leaf(stepRef: "nope") } }
    };
    var ifStep = new SequenceStep {
      StepId = "if-1", StepType = SequenceStepType.If, If = new IfConfig { Condition = Leaf(stepRef: "nope2") }
    };

    var errors = Validate(CommandStep("send-1"), loop, ifStep);

    errors.Should().Contain(e => e.Contains("unknown prior step 'nope'", System.StringComparison.Ordinal));
    errors.Should().Contain(e => e.Contains("unknown prior step 'nope2'", System.StringComparison.Ordinal));
  }

  [Fact]
  public void ValidateReferencesReturnsNothingWithoutTheCondition() {
    CommandStepOutcomeRules.ValidateReferences(new[] { CommandStep("a") }).Should().BeEmpty();
  }

  [Fact]
  public void ValidateReferencesReturnsTheErrorsOfABadReference() {
    CommandStepOutcomeRules.ValidateReferences(new[] { CommandStep("a"), CommandStep("b", Leaf(stepRef: "zzz")) })
      .Should().ContainSingle().Which.Should().Contain("unknown prior step 'zzz'");
  }

  [Fact]
  public async System.Threading.Tasks.Task TheRepositoryRefusesABadOrderAndKeepsAGoodOne() {
    var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gamebot-seq-{System.Guid.NewGuid():N}");
    try {
      var repo = new FileSequenceRepository(root);
      CommandSequence Build(string id, int order) {
        var sequence = new CommandSequence { Id = id, Name = id };
        sequence.SetSteps(new[] { CommandStep("send-1"), CommandStep("next", Leaf(order: order)) });
        return sequence;
      }

      var bad = async () => await repo.CreateAsync(Build("bad", 0));
      await bad.Should().ThrowAsync<System.InvalidOperationException>();

      await repo.CreateAsync(Build("good", 12));
      var loaded = await repo.GetAsync("good");
      loaded!.Steps[1].Condition.Should().BeOfType<CommandStepOutcomeStepCondition>()
        .Which.CommandStepOrder.Should().Be(12);
    }
    finally {
      if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
    }
  }
}
