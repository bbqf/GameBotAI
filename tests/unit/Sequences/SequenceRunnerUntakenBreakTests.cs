using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 117 (issue #250): a <c>commandOutcome</c> condition that names a Break that did not run
/// evaluates as <c>no_break</c>. A Break that ran keeps the outcome that it recorded last. A reference
/// to a step that is not a Break, or to an unknown step, still fails.
/// </summary>
public sealed class SequenceRunnerUntakenBreakTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  // ── Builders ─────────────────────────────────────────────────────────────

  private static CommandOutcomeStepCondition Outcome(string stepRef, string expectedState, bool negate = false) =>
      new() { StepRef = stepRef, ExpectedState = expectedState, Negate = negate };

  /// <summary>The If conditions read the outcome of <c>probe</c>, which is <c>success</c>.</summary>
  private static CommandOutcomeStepCondition NotTaken => Outcome("probe", "failed");
  private static CommandOutcomeStepCondition Taken => Outcome("probe", "success");

  private static SequenceStep Tap(string stepId, SequenceStepCondition? condition = null, bool requireDispatch = false) => new() {
    StepId = stepId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 2 } },
    Condition = condition,
    RequireDispatch = requireDispatch
  };

  private static SequenceStep Brk(string stepId, SequenceStepCondition? condition = null) =>
      new() { StepId = stepId, StepType = SequenceStepType.Break, BreakCondition = condition };

  private static SequenceStep If(string stepId, SequenceStepCondition condition, params SequenceStep[] body) => new() {
    StepId = stepId,
    StepType = SequenceStepType.If,
    If = new IfConfig { Condition = condition },
    Body = body
  };

  private static SequenceStep CountLoop(string stepId, int count, params SequenceStep[] body) => new() {
    StepId = stepId,
    StepType = SequenceStepType.Loop,
    Loop = new CountLoopConfig { Count = count, MaxIterations = Math.Max(count, 1) },
    Body = body
  };

  private static SequenceStep[] Ordered(params SequenceStep[] steps) {
    for (var i = 0; i < steps.Length; i++) steps[i].Order = i;
    return steps;
  }

  private static async Task<SequenceExecutionResult> RunAsync(
      IReadOnlyList<SequenceStep> steps,
      Func<int, bool>? imageAnswer = null) {
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    sequence.SetSteps(steps.ToArray());
    var runner = new SequenceRunner(new StubRepo(sequence));
    var imageCalls = 0;

    return await runner.ExecuteAsync(
      sequence.Id,
      (_, _) => Task.CompletedTask,
      conditionEvaluator: (_, _) => Task.FromResult(imageAnswer?.Invoke(++imageCalls) ?? true),
      actionDispatcher: (_, _) => Task.FromResult(new ActionDispatchResult("executed", null)),
      ct: CancellationToken.None);
  }

  private static void ShouldHaveNoUnavailableMessage(SequenceExecutionResult result) =>
      result.Steps.Should().NotContain(s => s.Message != null && s.Message.Contains("is unavailable", StringComparison.Ordinal));

  private static StepResult Step(SequenceExecutionResult result, string id) =>
      result.Steps.Should().ContainSingle(s => s.CommandId == id || s.StepId == id).Subject;

  /// <summary>A count Loop with an If branch that does not run, and a Break <c>brk</c> in the branch.</summary>
  private static IReadOnlyList<SequenceStep> UntakenBranch(SequenceStep after) => Ordered(
      Tap("probe"),
      CountLoop("loop1", 2, Ordered(If("if1", NotTaken, Ordered(Brk("brk"))), Tap("settle"))),
      after);

  // ── US1: a Break in an If branch that did not run ────────────────────────

  [Fact]
  public async Task NoBreakGuardOnABreakInAnIfBranchThatDidNotRunRunsTheStep() {
    var result = await RunAsync(UntakenBranch(Tap("after", Outcome("brk", BreakOutcomes.NoBreak))));

    result.Status.Should().Be("Succeeded");
    ShouldHaveNoUnavailableMessage(result);
    Step(result, "after").Status.Should().Be("Succeeded");
    result.Steps.Should().NotContain(s => s.CommandId == "brk");
  }

  [Fact]
  public async Task BreakGuardOnABreakInAnIfBranchThatDidNotRunSkipsTheStep() {
    var result = await RunAsync(UntakenBranch(Tap("after", Outcome("brk", BreakOutcomes.Break))));

    result.Status.Should().Be("Succeeded");
    ShouldHaveNoUnavailableMessage(result);
    var after = Step(result, "after");
    after.Status.Should().Be("Skipped");
    after.ConditionResult.Should().Be("false");
  }

  [Fact]
  public async Task NegatedBreakGuardOnABreakInAnIfBranchThatDidNotRunRunsTheStep() {
    var result = await RunAsync(UntakenBranch(Tap("after", Outcome("brk", BreakOutcomes.Break, negate: true))));

    result.Status.Should().Be("Succeeded");
    ShouldHaveNoUnavailableMessage(result);
    Step(result, "after").Status.Should().Be("Succeeded");
  }

  private static IReadOnlyList<SequenceStep> Issue250Reproduction(SequenceStepCondition ifEmptyCondition) => Ordered(
      Tap("probe"),
      CountLoop("book", 3, Ordered(
        If("if-empty", ifEmptyCondition, Ordered(Tap("book-reset"), Brk("brk-empty"))),
        If("if-wait", NotTaken, Ordered(Tap("book-wait"), Brk("brk-wait"))),
        Tap("settle"))),
      Tap("fail-no-booking", new AllStepCondition {
        Children = new SequenceStepCondition[] {
          Outcome("brk-empty", BreakOutcomes.Break, negate: true),
          Outcome("brk-wait", BreakOutcomes.Break, negate: true)
        }
      }, requireDispatch: true));

  [Fact]
  public async Task Issue250ReproductionRunsTheGuardedStepWhenNoBranchRuns() {
    var result = await RunAsync(Issue250Reproduction(NotTaken));

    result.Status.Should().Be("Succeeded");
    ShouldHaveNoUnavailableMessage(result);
    var guarded = Step(result, "fail-no-booking");
    guarded.Status.Should().Be("Succeeded");
    guarded.ConditionResult.Should().Be("true");
    result.Steps.Should().NotContain(s => s.CommandId == "book-reset" || s.CommandId == "book-wait");
  }

  // ── US2: a Break that ran keeps its outcome ──────────────────────────────

  [Fact]
  public async Task Issue250ReproductionSkipsTheGuardedStepWhenTheBreakFired() {
    var result = await RunAsync(Issue250Reproduction(Taken));

    result.Status.Should().Be("Succeeded");
    var guarded = Step(result, "fail-no-booking");
    guarded.Status.Should().Be("Skipped");
    guarded.ConditionResult.Should().Be("false");
  }

  [Fact]
  public async Task BreakThatFiredReadsBreak() {
    var steps = Ordered(
      Tap("probe"),
      CountLoop("loop1", 3, If("if1", Taken, Brk("brk"))),
      Tap("after", Outcome("brk", BreakOutcomes.Break)));

    var result = await RunAsync(steps);

    result.Status.Should().Be("Succeeded");
    Step(result, "after").Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task BreakThatRanAndDidNotFireReadsNoBreak() {
    var steps = Ordered(
      Tap("probe"),
      CountLoop("loop1", 2, If("if1", Taken, Brk("brk", NotTaken))),
      Tap("after", Outcome("brk", BreakOutcomes.NoBreak)));

    var result = await RunAsync(steps);

    result.Status.Should().Be("Succeeded");
    result.Steps.Should().Contain(s => s.CommandId == "brk" && s.ActionOutcome == BreakOutcomes.NoBreak);
    Step(result, "after").Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task LastRecordedNoBreakStaysWhenTheBranchDoesNotRunInALaterIteration() {
    var steps = Ordered(
      Tap("probe"),
      CountLoop("loop1", 3, If("if1", new ImageVisibleStepCondition { ImageId = "img" }, Brk("brk", NotTaken))),
      Tap("after", Outcome("brk", BreakOutcomes.NoBreak)));

    // The image is visible for the first call only, so the branch runs in iteration 1 only.
    var result = await RunAsync(steps, imageAnswer: call => call == 1);

    result.Status.Should().Be("Succeeded");
    result.Steps.Should().ContainSingle(s => s.CommandId == "brk");
    Step(result, "after").Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task LastRecordedBreakStaysAfterTheBreakFiredInIterationOne() {
    var steps = Ordered(
      Tap("probe"),
      CountLoop("loop1", 3, If("if1", new ImageVisibleStepCondition { ImageId = "img" }, Brk("brk"))),
      Tap("after", Outcome("brk", BreakOutcomes.Break)));

    var result = await RunAsync(steps, imageAnswer: call => call == 1);

    result.Status.Should().Be("Succeeded");
    Step(result, "after").Status.Should().Be("Succeeded");
  }

  // ── US3: other cases in which a Break did not run, and the errors that stay ──

  [Fact]
  public async Task BreakInALoopBodyWithZeroIterationsReadsNoBreak() {
    var steps = Ordered(
      CountLoop("loop1", 0, Brk("brk")),
      Tap("after", Outcome("brk", BreakOutcomes.NoBreak)));

    var result = await RunAsync(steps);

    result.Status.Should().Be("Succeeded");
    ShouldHaveNoUnavailableMessage(result);
    Step(result, "after").Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task BreakInATopLevelIfBranchThatDidNotRunReadsNoBreak() {
    var steps = Ordered(
      Tap("probe"),
      If("if1", NotTaken, Brk("brk")),
      Tap("after", Outcome("brk", BreakOutcomes.NoBreak)));

    var result = await RunAsync(steps);

    result.Status.Should().Be("Succeeded");
    ShouldHaveNoUnavailableMessage(result);
    Step(result, "after").Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task WhileConditionThatReadsABreakInItsOwnBodyRunsOneIteration() {
    var loop = new SequenceStep {
      Order = 0, StepId = "w1", StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = Outcome("brk", BreakOutcomes.NoBreak), MaxIterations = 5 },
      Body = Ordered(Tap("body-tap"), Brk("brk"))
    };

    var result = await RunAsync(new[] { loop });

    result.Status.Should().Be("Succeeded");
    ShouldHaveNoUnavailableMessage(result);
    result.Steps.Should().ContainSingle(s => s.CommandId == "body-tap");
    result.Steps.Should().ContainSingle(s => s.CommandId == "brk").Which.ActionOutcome.Should().Be(BreakOutcomes.Break);
  }

  [Fact]
  public async Task ReferenceToAnActionInAnIfBranchThatDidNotRunStillFails() {
    var steps = Ordered(
      Tap("probe"),
      CountLoop("loop1", 1, If("if1", NotTaken, Tap("then-tap"), Brk("brk"))),
      Tap("after", Outcome("then-tap", "success")));

    var result = await RunAsync(steps);

    result.Status.Should().Be("Failed");
    Step(result, "after").Message.Should().Be("Step 'after' commandOutcome reference 'then-tap' is unavailable");
  }

  [Fact]
  public async Task UnknownReferenceStillFails() {
    var steps = Ordered(
      Tap("probe"),
      CountLoop("loop1", 1, Brk("brk")),
      Tap("after", Outcome("no-such-step", "success")));

    var result = await RunAsync(steps);

    result.Status.Should().Be("Failed");
    Step(result, "after").Message.Should().Be("Step 'after' commandOutcome reference 'no-such-step' is unavailable");
  }
}
