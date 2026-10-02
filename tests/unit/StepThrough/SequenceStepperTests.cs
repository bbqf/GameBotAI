using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepperTestKit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

/// <summary>Cursor and frame rules of the stepper (feature 127, FR-004a, FR-005, FR-006).</summary>
public sealed class SequenceStepperTests {
  private static async Task<(List<string> Executed, StepperState State, List<HistoryEntry> Steps)> RunAsync(
      CommandSequence sequence,
      Dictionary<string, Queue<bool>>? answers = null,
      Func<string, ParameterScope, Task<CommandDispatchOutcome>>? dispatcher = null) {
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var steps = await RunToEndAsync(stepper, sequence, state, Deps(executed, answers, dispatcher));
    return (executed, state, steps);
  }

  [Fact]
  public async Task LinearSequenceRunsOneStepPerCallInOrderAndEndsComplete() {
    var sequence = Sequence(Cmd(0, "a"), Cmd(1, "b"), Cmd(2, "c"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = Deps(executed);

    state.Cursor.Should().Be("0");
    var first = await stepper.RunNextAsync(sequence, state, deps);
    executed.Should().Equal("a");
    first.Should().ContainSingle().Which.Status.Should().Be("Succeeded");
    state.Cursor.Should().Be("1");

    await stepper.RunNextAsync(sequence, state, deps);
    state.Cursor.Should().Be("2");
    await stepper.RunNextAsync(sequence, state, deps);

    executed.Should().Equal("a", "b", "c");
    state.Cursor.Should().BeNull();
    state.History.Select(h => h.Seq).Should().Equal(1, 2, 3);
  }

  [Fact]
  public async Task RunNextOnACompleteSequenceDoesNothing() {
    var sequence = Sequence(Cmd(0, "a"));
    var (executed, state, _) = await RunAsync(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(executed));

    produced.Should().BeEmpty();
    executed.Should().Equal("a");
  }

  [Fact]
  public async Task CountLoopRunsEachBodyActionAsOwnStepWithIterationNumbers() {
    var sequence = Sequence(
      CountLoop(0, "loop", 2, Cmd(0, "in1"), Cmd(1, "in2")),
      Cmd(1, "after"));

    var (executed, state, steps) = await RunAsync(sequence);

    executed.Should().Equal("in1", "in2", "in1", "in2", "after");
    steps.Select(s => s.Iteration).Should().Equal(1, 1, 2, 2, null);
    state.Frames.Should().BeEmpty();
    state.History.Should().Contain(h => h.Kind == HistoryKind.Enter && h.Path == "0");
    state.History.Should().Contain(h => h.Kind == HistoryKind.Exit && h.Path == "0" && h.Outcome == "completed");
  }

  [Fact]
  public async Task CountLoopWithZeroCountSkipsTheBody() {
    var sequence = Sequence(CountLoop(0, "loop", 0, Cmd(0, "in")), Cmd(1, "after"));

    var (executed, _, _) = await RunAsync(sequence);

    executed.Should().Equal("after");
  }

  [Fact]
  public async Task WhileLoopChecksItsConditionBeforeEachIteration() {
    var sequence = Sequence(WhileLoop(0, "loop", null, false, Cmd(0, "in")), Cmd(1, "after"));
    var answers = Answers(("w-loop", new[] { true, true, false }));

    var (executed, _, steps) = await RunAsync(sequence, answers);

    executed.Should().Equal("in", "in", "after");
    steps.Select(s => s.Iteration).Should().Equal(1, 2, null);
  }

  [Fact]
  public async Task WhileLoopWithFalseConditionOnEntryIsSkipped() {
    var sequence = Sequence(WhileLoop(0, "loop", null, false, Cmd(0, "in")), Cmd(1, "after"));

    var (executed, state, _) = await RunAsync(sequence);

    executed.Should().Equal("after");
    state.History.Should().Contain(h => h.Kind == HistoryKind.Enter && h.Status == "Skipped");
  }

  [Fact]
  public async Task RepeatUntilRunsBodyFirstAndChecksItsExitConditionAfterEachIteration() {
    var sequence = Sequence(RepeatUntil(0, "loop", Cmd(0, "in")), Cmd(1, "after"));
    var answers = Answers(("u-loop", new[] { false, false, true }));

    var (executed, _, steps) = await RunAsync(sequence, answers);

    executed.Should().Equal("in", "in", "in", "after");
    steps.Select(s => s.Iteration).Should().Equal(1, 2, 3, null);
  }

  [Fact]
  public async Task WhileLoopThatHitsMaxIterationsFailsWhenExitOnMaxIsOff() {
    var sequence = Sequence(WhileLoop(0, "loop", 3, false, Cmd(0, "in")), Cmd(1, "after"));
    var answers = Answers(("w-loop", Enumerable.Repeat(true, 20).ToArray()));

    var (executed, state, _) = await RunAsync(sequence, answers);

    executed.Should().Equal("in", "in", "in", "after");
    var exit = state.History.Single(h => h.Kind == HistoryKind.Exit);
    exit.Status.Should().Be("Failed");
    exit.Message.Should().Contain("exceeded maximum iterations (3)");
  }

  [Fact]
  public async Task WhileLoopThatHitsMaxIterationsEndsNormallyWhenExitOnMaxIsOn() {
    var sequence = Sequence(WhileLoop(0, "loop", 2, true, Cmd(0, "in")), Cmd(1, "after"));
    var answers = Answers(("w-loop", Enumerable.Repeat(true, 20).ToArray()));

    var (executed, state, _) = await RunAsync(sequence, answers);

    executed.Should().Equal("in", "in", "after");
    var exit = state.History.Single(h => h.Kind == HistoryKind.Exit);
    exit.Status.Should().Be("Succeeded");
    exit.Outcome.Should().Be("exhausted");
  }

  [Fact]
  public async Task RepeatUntilStopsAtMaxIterations() {
    var loop = RepeatUntil(0, "loop", Cmd(0, "in"));
    loop.Loop!.MaxIterations = 2;
    var sequence = Sequence(loop, Cmd(1, "after"));

    var (executed, state, _) = await RunAsync(sequence);

    executed.Should().Equal("in", "in", "after");
    state.History.Single(h => h.Kind == HistoryKind.Exit).Status.Should().Be("Failed");
  }

  [Fact]
  public async Task IfStepRunsThenBranchWhenConditionIsTrue() {
    var sequence = Sequence(
      If(0, "if", new[] { Cmd(0, "then1"), Cmd(1, "then2") }, new[] { Cmd(0, "else1") }),
      Cmd(1, "after"));

    var (executed, state, _) = await RunAsync(sequence, Answers(("i-if", new[] { true })));

    executed.Should().Equal("then1", "then2", "after");
    var enter = state.History.Single(h => h.Kind == HistoryKind.Enter);
    enter.Outcome.Should().Be("then");
  }

  [Fact]
  public async Task IfStepRunsElseBranchWhenConditionIsFalse() {
    var sequence = Sequence(
      If(0, "if", new[] { Cmd(0, "then1") }, new[] { Cmd(0, "else1"), Cmd(1, "else2") }),
      Cmd(1, "after"));

    var (executed, state, _) = await RunAsync(sequence, Answers(("i-if", new[] { false })));

    executed.Should().Equal("else1", "else2", "after");
    state.History.Single(h => h.Kind == HistoryKind.Enter).Outcome.Should().Be("else");
  }

  [Fact]
  public async Task IfStepWithNoMatchingBranchStepsMovesToTheNextStep() {
    var sequence = Sequence(If(0, "if", new[] { Cmd(0, "then1") }), Cmd(1, "after"));

    var (executed, state, _) = await RunAsync(sequence, Answers(("i-if", new[] { false })));

    executed.Should().Equal("after");
    state.History.Single(h => h.Kind == HistoryKind.Enter).Outcome.Should().Be("none");
  }

  [Fact]
  public async Task IfStepInsideALoopIsEvaluatedAgainInEachIteration() {
    var sequence = Sequence(CountLoop(0, "loop", 3,
      If(0, "if", new[] { Cmd(0, "then1") }, new[] { Cmd(0, "else1") })));
    var answers = Answers(("i-if", new[] { true, false, true }));

    var (executed, _, steps) = await RunAsync(sequence, answers);

    executed.Should().Equal("then1", "else1", "then1");
    steps.Select(s => s.Iteration).Should().Equal(1, 2, 3);
  }

  [Fact]
  public async Task BreakInsideALoopEndsTheLoopAndContinuesAfterIt() {
    var sequence = Sequence(
      CountLoop(0, "loop", 5, Cmd(0, "in1"), Break(1, "brk"), Cmd(2, "in2")),
      Cmd(1, "after"));

    var (executed, state, steps) = await RunAsync(sequence);

    executed.Should().Equal("in1", "after");
    steps.Select(s => s.Outcome).Should().Contain(BreakOutcomes.Break);
    state.History.Single(h => h.Kind == HistoryKind.Exit).Outcome.Should().Be(BreakOutcomes.Break);
    state.Frames.Should().BeEmpty();
  }

  [Fact]
  public async Task ConditionalBreakThatDoesNotFireKeepsTheLoopGoing() {
    var sequence = Sequence(
      CountLoop(0, "loop", 2, Break(0, "brk", Image("b")), Cmd(1, "in")));
    var answers = Answers(("b", new[] { false, true }));

    var (executed, _, steps) = await RunAsync(sequence, answers);

    executed.Should().Equal("in");
    steps.Where(s => s.StepId == "brk").Select(s => s.Outcome)
      .Should().Equal(BreakOutcomes.NoBreak, BreakOutcomes.Break);
  }

  [Fact]
  public async Task BreakInsideAnIfBranchEndsTheEnclosingLoop() {
    var sequence = Sequence(
      CountLoop(0, "loop", 3, If(0, "if", new[] { Break(0, "brk") }), Cmd(1, "in")),
      Cmd(1, "after"));

    var (executed, state, _) = await RunAsync(sequence, Answers(("i-if", new[] { true })));

    executed.Should().Equal("after");
    state.Frames.Should().BeEmpty();
  }

  [Fact]
  public async Task FailedStepIsRecordedAndTheCursorMovesToTheNextStep() {
    var sequence = Sequence(Cmd(0, "a"), Cmd(1, "b"), Cmd(2, "c"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = Deps(executed, commandDispatcher: (id, _) =>
      id == "b" ? throw new InvalidOperationException("boom") : Task.FromResult(CommandDispatchOutcome.Executed));

    await stepper.RunNextAsync(sequence, state, deps);
    var failed = await stepper.RunNextAsync(sequence, state, deps);

    var entry = failed.Should().ContainSingle().Subject;
    entry.Status.Should().Be("Failed");
    entry.Message.Should().Be("boom");
    entry.Notes.Should().Contain(SequenceStepper.SequenceEndNote);
    state.Cursor.Should().Be("2");
  }

  [Fact]
  public async Task StepWithAFalseGuardIsRecordedAsSkippedAndDoesNotRun() {
    var sequence = Sequence(Cmd(0, "a", Image("g")), Cmd(1, "b"));

    var (executed, _, steps) = await RunAsync(sequence, Answers(("g", new[] { false })));

    executed.Should().Equal("b");
    steps[0].Status.Should().Be("Skipped");
    steps[0].Path.Should().Be("0");
  }

  [Fact]
  public async Task DeletedCommandReferenceFailsWithTheSameMessageAsARealRun() {
    const string message = "Command 'gone' was not found; the sequence step references a missing command.";
    var sequence = Sequence(Cmd(0, "gone"));

    var (_, _, steps) = await RunAsync(sequence, dispatcher: (_, _) => throw new InvalidOperationException(message));

    steps.Should().ContainSingle();
    steps[0].Status.Should().Be("Failed");
    steps[0].Message.Should().Be(message);
  }

  [Fact]
  public async Task StepKeepsItsOwnTimeoutAndFailsWhenItsGateNeverOpens() {
    var step = Cmd(0, "a");
    step.Gate = new GateConfig { TargetId = "img" };
    step.TimeoutMs = 150;
    var sequence = Sequence(step, Cmd(1, "b"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = Deps(executed, gate: (_, _) => Task.FromResult(false));

    var produced = await stepper.RunNextAsync(sequence, state, deps);

    produced.Should().ContainSingle().Which.Status.Should().Be("Failed");
    executed.Should().BeEmpty();
    state.Cursor.Should().Be("1");
  }

  [Fact]
  public async Task NoTimeLimitAppliesBetweenSteps() {
    var sequence = Sequence(Cmd(0, "a"), Cmd(1, "b"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = Deps(executed);

    await stepper.RunNextAsync(sequence, state, deps);
    await Task.Delay(400);
    await stepper.RunNextAsync(sequence, state, deps);

    executed.Should().Equal("a", "b");
    state.Cursor.Should().BeNull();
  }

  [Fact]
  public async Task CancelledStepThrowsAndLeavesTheCursorOnTheStep() {
    var sequence = Sequence(Cmd(0, "a"), Cmd(1, "b"));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    using var cts = new CancellationTokenSource();
    var deps = Deps(new List<string>(), commandDispatcher: (_, _) => {
      cts.Cancel();
      throw new OperationCanceledException(cts.Token);
    });

    Func<Task> act = () => stepper.RunNextAsync(sequence, state, deps, cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
    state.Cursor.Should().Be("0");
    state.History.Should().BeEmpty();
  }

  [Fact]
  public async Task OutcomesFromEarlierStepsAreAvailableToLaterConditions() {
    var sequence = Sequence(
      Cmd(0, "a"),
      Cmd(1, "b", new CommandOutcomeStepCondition { StepRef = "a", ExpectedState = "success" }));

    var (executed, _, _) = await RunAsync(sequence);

    executed.Should().Equal("a", "b");
  }

  [Fact]
  public async Task ConditionOnAnOutcomeThatWasNeverSetFailsLikeARealRun() {
    var sequence = Sequence(
      Cmd(0, "a"),
      Cmd(1, "b", new CommandOutcomeStepCondition { StepRef = "a", ExpectedState = "success" }));
    var state = Start(sequence);
    state.Cursor = "1";
    var executed = new List<string>();
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(executed));

    produced.Should().ContainSingle().Which.Status.Should().Be("Failed");
    executed.Should().BeEmpty();
  }

  [Fact]
  public async Task AuthorParameterValuesReachTheStepScope() {
    var step = Cmd(0, "a");
    step.Action!.Parameters["x"] = "{{n}}";
    var sequence = Sequence(step);
    sequence.Parameters.Add(new GameBot.Domain.Parameters.ParameterDeclaration { Name = "n", Default = "5" });
    var state = Start(sequence);
    state.ParameterValues["n"] = "7";
    string? seen = null;
    var stepper = new SequenceStepper(Runner(sequence));
    var deps = new StepperDependencies {
      ExecuteCommandAsync = (_, _) => Task.CompletedTask,
      CommandDispatcher = (_, scope) => {
        scope.TryResolve("n", out var value);
        seen = value.Text;
        return Task.FromResult(CommandDispatchOutcome.Executed);
      }
    };

    await stepper.RunNextAsync(sequence, state, deps);

    seen.Should().Be("7");
  }

  [Fact]
  public async Task HistoryIsCappedAtOneThousandEntriesAndDropsTheOldestFirst() {
    var sequence = Sequence(CountLoop(0, "loop", 1100, Cmd(0, "in")));

    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));
    await RunToEndAsync(stepper, sequence, state, Deps(new List<string>()), maxCalls: 5000);

    state.History.Count.Should().Be(StepperState.HistoryCap);
    state.History[0].Seq.Should().BeGreaterThan(1);
    state.History.Select(h => h.Seq).Should().BeInAscendingOrder();
  }
}
