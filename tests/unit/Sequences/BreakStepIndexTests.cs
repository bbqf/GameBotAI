using System;
using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 117: <see cref="BreakStepIndex"/> finds each Break step of a sequence and gives it the
/// default outcome <c>no_break</c>.
/// </summary>
public sealed class BreakStepIndexTests {
  private static SequenceStep Tap(string stepId) => new() {
    StepId = stepId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 2 } }
  };

  private static SequenceStep Brk(string stepId) => new() { StepId = stepId, StepType = SequenceStepType.Break };

  private static SequenceStep Loop(string stepId, params SequenceStep[] body) => new() {
    StepId = stepId,
    StepType = SequenceStepType.Loop,
    Loop = new CountLoopConfig { Count = 1 },
    Body = body
  };

  private static SequenceStep If(string stepId, SequenceStep[] body, SequenceStep[]? elseBody = null) => new() {
    StepId = stepId,
    StepType = SequenceStepType.If,
    If = new IfConfig { Condition = new CommandOutcomeStepCondition { StepRef = "probe", ExpectedState = "success" } },
    Body = body,
    ElseBody = elseBody
  };

  [Fact]
  public void CollectsABreakInALoopBodyAndInEachIfBranch() {
    var steps = new[] {
      Loop("loop1",
        Brk("brk-direct"),
        If("if1", new[] { Brk("brk-then") }, new[] { Brk("brk-else") }))
    };

    var ids = BreakStepIndex.CollectBreakStepIds(steps);

    ids.Should().BeEquivalentTo(new[] { "brk-direct", "brk-then", "brk-else" });
  }

  [Fact]
  public void DoesNotCollectTheIdsOfActionLoopAndIfSteps() {
    var steps = new[] {
      Tap("probe"),
      Loop("loop1", If("if1", new[] { Tap("then-tap"), Brk("brk") }))
    };

    var ids = BreakStepIndex.CollectBreakStepIds(steps);

    ids.Should().BeEquivalentTo(new[] { "brk" });
    ids.Should().NotContain(new[] { "probe", "loop1", "if1", "then-tap" });
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  public void DoesNotCollectABreakWithAnEmptyStepId(string? stepId) {
    var steps = new[] { Loop("loop1", new SequenceStep { StepId = stepId!, StepType = SequenceStepType.Break }) };

    BreakStepIndex.CollectBreakStepIds(steps).Should().BeEmpty();
  }

  [Fact]
  public void DoesNotCollectAnIdThatAlsoNamesAStepOfADifferentType() {
    var steps = new[] {
      Tap("dup"),
      Loop("loop1", Brk("DUP"), Brk("brk"))
    };

    BreakStepIndex.CollectBreakStepIds(steps).Should().BeEquivalentTo(new[] { "brk" });
  }

  [Fact]
  public void TheSetIsCaseInsensitive() {
    var ids = BreakStepIndex.CollectBreakStepIds(new[] { Loop("loop1", Brk("brk")) });

    ids.Contains("BRK").Should().BeTrue();
  }

  [Fact]
  public void SeedAddsNoBreakForEachBreakAndKeepsAValueThatIsAlreadyInTheMap() {
    var steps = new[] { Loop("loop1", Brk("brk"), If("if1", new[] { Brk("brk-then") })) };
    var outcomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      ["brk"] = BreakOutcomes.Break
    };

    BreakStepIndex.SeedNoBreakOutcomes(outcomes, steps);

    outcomes.Should().HaveCount(2);
    outcomes["brk"].Should().Be(BreakOutcomes.Break);
    outcomes["brk-then"].Should().Be(BreakOutcomes.NoBreak);
  }

  [Fact]
  public void NullArgumentsThrow() {
    var outcomes = new Dictionary<string, string>();
    var steps = Array.Empty<SequenceStep>();

    FluentActions.Invoking(() => BreakStepIndex.CollectBreakStepIds(null!)).Should().Throw<ArgumentNullException>();
    FluentActions.Invoking(() => BreakStepIndex.SeedNoBreakOutcomes(null!, steps)).Should().Throw<ArgumentNullException>();
    FluentActions.Invoking(() => BreakStepIndex.SeedNoBreakOutcomes(outcomes, null!)).Should().Throw<ArgumentNullException>();
  }
}
