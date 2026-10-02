using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepperTestKit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

/// <summary>Manual selection of the next step (feature 127, FR-007, FR-013a).</summary>
public sealed class SequenceStepperSelectTests {
  [Fact]
  public async Task SelectMovesTheCursorAndTheNextRunExecutesThatStep() {
    var sequence = Sequence(Cmd(0, "a"), Cmd(1, "b"), Cmd(2, "c"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = Deps(executed);

    SequenceStepper.Select(sequence, state, "2").Should().Be(StepSelectResult.Ok);
    await stepper.RunNextAsync(sequence, state, deps);
    SequenceStepper.Select(sequence, state, "0").Should().Be(StepSelectResult.Ok);
    await stepper.RunNextAsync(sequence, state, deps);

    executed.Should().Equal("c", "a");
    state.History.Select(h => h.Path).Should().Equal("2", "0");
    state.Cursor.Should().Be("1");
  }

  [Fact]
  public async Task AfterAManualRunTheCursorFollowsTheNormalOrderFromThatStep() {
    var sequence = Sequence(Cmd(0, "a"), Cmd(1, "b"), Cmd(2, "c"));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    SequenceStepper.Select(sequence, state, "1");
    await stepper.RunNextAsync(sequence, state, Deps(new List<string>()));

    state.Cursor.Should().Be("2");
  }

  [Fact]
  public void SelectIntoALoopBodyOpensTheLoopFrameAtIterationOne() {
    var sequence = Sequence(CountLoop(0, "loop", 3, Cmd(0, "in1"), Cmd(1, "in2")));
    var state = Start(sequence);

    SequenceStepper.Select(sequence, state, "0/body/1").Should().Be(StepSelectResult.Ok);

    state.Frames.Should().ContainSingle();
    state.Frames[0].Should().Be(new Frame("0", FrameKind.LoopCount, 1, 3));
    state.Cursor.Should().Be("0/body/1");
  }

  [Fact]
  public void SelectIntoAnIfBranchOpensAFrameForTheBranch() {
    var sequence = Sequence(If(0, "if", new[] { Cmd(0, "t") }, new[] { Cmd(0, "e") }));
    var state = Start(sequence);

    SequenceStepper.Select(sequence, state, "0/else/0").Should().Be(StepSelectResult.Ok);

    state.Frames.Single().Kind.Should().Be(FrameKind.IfElse);
  }

  [Fact]
  public void SelectInsideAnIfInsideALoopOpensBothFrames() {
    var sequence = Sequence(CountLoop(0, "loop", 2, If(0, "if", new[] { Cmd(0, "t") })));
    var state = Start(sequence);

    SequenceStepper.Select(sequence, state, "0/body/0/body/0").Should().Be(StepSelectResult.Ok);

    state.Frames.Select(f => f.Kind).Should().Equal(FrameKind.LoopCount, FrameKind.IfThen);
  }

  [Fact]
  public async Task RunningTheLastBodyStepAfterAJumpContinuesWithTheNextIteration() {
    var sequence = Sequence(CountLoop(0, "loop", 2, Cmd(0, "in1"), Cmd(1, "in2")), Cmd(1, "after"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    SequenceStepper.Select(sequence, state, "0/body/1");
    var steps = await RunToEndAsync(stepper, sequence, state, Deps(executed));

    executed.Should().Equal("in2", "in1", "in2", "after");
    steps.Select(s => s.Iteration).Should().Equal(1, 2, 2, null);
  }

  [Fact]
  public async Task AFailedStepCanBeSelectedAgainAndRunsAgainWithANewHistoryEntry() {
    var sequence = Sequence(Cmd(0, "a"), Cmd(1, "b"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var attempts = 0;
    var deps = Deps(executed, commandDispatcher: (id, _) => {
      if (id == "a" && ++attempts == 1) throw new InvalidOperationException("first try fails");
      return Task.FromResult(CommandDispatchOutcome.Executed);
    });

    await stepper.RunNextAsync(sequence, state, deps);
    SequenceStepper.Select(sequence, state, "0");
    await stepper.RunNextAsync(sequence, state, deps);

    state.History.Select(h => h.Status).Should().Equal("Failed", "Succeeded");
    state.History.Select(h => h.Seq).Should().Equal(1, 2);
  }

  [Fact]
  public void HeaderRowIsNotSelectableAndTheStateStaysTheSame() {
    var sequence = Sequence(CountLoop(0, "loop", 2, Cmd(0, "in")), If(1, "if", new[] { Cmd(0, "t") }));
    var state = Start(sequence);
    state.Cursor = "1";

    SequenceStepper.Select(sequence, state, "0").Should().Be(StepSelectResult.NotSelectable);
    SequenceStepper.Select(sequence, state, "1").Should().Be(StepSelectResult.NotSelectable);

    state.Cursor.Should().Be("1");
    state.Frames.Should().BeEmpty();
  }

  [Theory]
  [InlineData("7")]
  [InlineData("0/body/5")]
  [InlineData("nope")]
  [InlineData("")]
  public void UnknownPathIsRefused(string path) {
    var sequence = Sequence(CountLoop(0, "loop", 2, Cmd(0, "in")));
    var state = Start(sequence);

    SequenceStepper.Select(sequence, state, path).Should().Be(StepSelectResult.UnknownStep);

    state.Cursor.Should().Be("0");
  }

  [Fact]
  public async Task StartingAtAMiddleStepLeavesTheOutcomesOfSkippedStepsUnset() {
    var sequence = Sequence(
      Cmd(0, "a"),
      Cmd(1, "b", new GameBot.Domain.Commands.CommandOutcomeStepCondition { StepRef = "a", ExpectedState = "success" }));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    SequenceStepper.Select(sequence, state, "1");
    var produced = await stepper.RunNextAsync(sequence, state, Deps(new List<string>()));

    produced.Single().Status.Should().Be("Failed");
  }

  [Fact]
  public async Task RestartClearsHistoryFramesAndOutcomesButKeepsParameterValues() {
    var sequence = Sequence(CountLoop(0, "loop", 2, Cmd(0, "in")));
    var state = Start(sequence);
    state.ParameterValues["n"] = "3";
    var stepper = new SequenceStepper(Runner(sequence));
    await stepper.RunNextAsync(sequence, state, Deps(new List<string>()));
    state.History.Should().NotBeEmpty();
    state.Frames.Should().NotBeEmpty();

    state.Restart(sequence);

    state.History.Should().BeEmpty();
    state.Frames.Should().BeEmpty();
    state.Outcomes.Should().BeEmpty();
    state.ParameterValues.Should().ContainKey("n");
    state.Cursor.Should().Be("0");
  }

  [Fact]
  public async Task SequenceNumbersKeepGrowingAfterARestart() {
    var sequence = Sequence(Cmd(0, "a"));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    await stepper.RunNextAsync(sequence, state, Deps(new List<string>()));

    state.Restart(sequence);
    await stepper.RunNextAsync(sequence, state, Deps(new List<string>()));

    state.History.Single().Seq.Should().Be(2);
  }
}
