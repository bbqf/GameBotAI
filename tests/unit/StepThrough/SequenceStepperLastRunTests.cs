using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepperTestKit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

/// <summary>A lastRun condition is always false in a step-through, and the entry says so (spec Clarifications).</summary>
public sealed class SequenceStepperLastRunTests {
  private static LastRunStepCondition LastRun(bool negate = false) => new() {
    Sequence = LastRunStepCondition.SelfSequence,
    Status = "success",
    Within = "01:00:00",
    Negate = negate
  };

  [Fact]
  public async Task LastRunGuardIsFalseAndTheEntryNotesThatItIsNotEvaluated() {
    var sequence = Sequence(Cmd(0, "a", LastRun()), Cmd(1, "b"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(executed));

    var entry = produced.Should().ContainSingle().Subject;
    entry.Status.Should().Be("Skipped");
    entry.Notes.Should().Contain(SequenceStepper.LastRunNote);
    executed.Should().BeEmpty();
    state.Cursor.Should().Be("1");
  }

  [Fact]
  public async Task LastRunInsideACompositeGuardAlsoGetsTheNote() {
    var composite = new AllStepCondition { Children = new SequenceStepCondition[] { Image("x"), LastRun() } };
    var sequence = Sequence(Cmd(0, "a", composite));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(new List<string>(), Answers(("x", new[] { true }))));

    produced.Single().Notes.Should().Contain(SequenceStepper.LastRunNote);
  }

  [Fact]
  public async Task StepWithoutALastRunConditionHasNoNote() {
    var sequence = Sequence(Cmd(0, "a", Image("x")));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(new List<string>(), Answers(("x", new[] { true }))));

    produced.Single().Notes.Should().BeEmpty();
  }

  [Fact]
  public async Task LastRunOnAnIfStepIsFalseSoTheElseBranchRuns() {
    var step = If(0, "if", new[] { Cmd(0, "then1") }, new[] { Cmd(0, "else1") });
    step.If!.Condition = LastRun();
    var sequence = Sequence(step);
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var steps = await RunToEndAsync(stepper, sequence, state, Deps(executed));

    executed.Should().Equal("else1");
    state.History.First(h => h.Kind == HistoryKind.Enter).Notes.Should().Contain(SequenceStepper.LastRunNote);
    steps.Should().ContainSingle();
  }
}
