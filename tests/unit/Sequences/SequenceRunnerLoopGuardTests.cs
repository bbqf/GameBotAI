using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Issue #232 (spec 110): the condition on a top-level Loop step is a guard. The runner evaluates
/// it one time before the first iteration. When it is false, the loop body does not run and the
/// Loop entry has status Skipped and condition result false.
/// </summary>
public sealed class SequenceRunnerLoopGuardTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _seq;
    public StubRepo(CommandSequence seq) { _seq = seq; }
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_seq);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() =>
        Task.FromResult<IReadOnlyList<CommandSequence>>(new[] { _seq }.ToList().AsReadOnly());
    public Task<CommandSequence> CreateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<CommandSequence> UpdateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static CommandSequence Sequence(params SequenceStep[] steps) {
    var seq = new CommandSequence {
      Id = "s",
      Name = "s",
      InterStepDelayRangeMs = new DelayRangeMs { Min = 0, Max = 0 }
    };
    seq.SetSteps(steps.ToList());
    return seq;
  }

  private static SequenceStep ActionStep(int order, string stepId, SequenceStepCondition? condition = null)
      => new() {
        Order = order,
        StepId = stepId,
        CommandId = stepId,
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = "tap" },
        Condition = condition
      };

  private static SequenceStep GuardedLoop(int order, SequenceStepCondition? guard, LoopConfig? loop = null)
      => new() {
        Order = order,
        StepId = "loop",
        StepType = SequenceStepType.Loop,
        Loop = loop ?? new CountLoopConfig { Count = 3 },
        Condition = guard,
        Body = new List<SequenceStep> { ActionStep(0, "inner") }
      };

  private static CommandOutcomeStepCondition OutcomeOfFirst(string expectedState)
      => new() { StepRef = "first", ExpectedState = expectedState };

  private static async Task<(SequenceExecutionResult Result, List<string> Executed)> RunAsync(
      CommandSequence sequence,
      Func<GameBot.Domain.Commands.Blocks.Condition, System.Threading.CancellationToken, Task<bool>>? conditionEvaluator = null) {
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));
    var result = await runner.ExecuteAsync(
        "s",
        (id, _) => { executed.Add(id); return Task.CompletedTask; },
        conditionEvaluator: conditionEvaluator).ConfigureAwait(false);
    return (result, executed);
  }

  private static StepResult LoopEntry(SequenceExecutionResult result)
      => result.Steps.Single(s => s.LoopIterations is not null);

  [Fact]
  public async Task FalseGuardSkipsLoopBodyAndRunsNextStep() {
    var (result, executed) = await RunAsync(Sequence(
        ActionStep(0, "first"),
        GuardedLoop(1, OutcomeOfFirst("failed")),
        ActionStep(2, "after")));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("first", "after");

    var loop = LoopEntry(result);
    loop.Status.Should().Be("Skipped");
    loop.LoopIterations.Should().BeEmpty();
    loop.ConditionType.Should().Be("commandOutcome");
    loop.ConditionResult.Should().Be("false");
    loop.Message.Should().Contain("condition is false");
  }

  [Fact]
  public async Task FalseCompositeGuardSkipsLoopBody() {
    var guard = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        OutcomeOfFirst("success"),
        new NoneStepCondition { Children = new SequenceStepCondition[] { OutcomeOfFirst("success") } }
      }
    };

    var (result, executed) = await RunAsync(Sequence(ActionStep(0, "first"), GuardedLoop(1, guard)));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("first");

    var loop = LoopEntry(result);
    loop.Status.Should().Be("Skipped");
    loop.ConditionType.Should().Be("all");
    loop.ConditionResult.Should().Be("false");
    loop.Message.Should().Contain("settled the guard");
  }

  [Fact]
  public async Task TrueGuardRunsAllIterations() {
    var (result, executed) = await RunAsync(Sequence(ActionStep(0, "first"), GuardedLoop(1, OutcomeOfFirst("success"))));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("first", "inner", "inner", "inner");

    var loop = LoopEntry(result);
    loop.Status.Should().Be("Succeeded");
    loop.LoopIterations.Should().HaveCount(3);
    loop.ConditionType.Should().Be("commandOutcome");
    loop.ConditionResult.Should().Be("true");
  }

  [Fact]
  public async Task LoopWithoutGuardRunsAsBefore() {
    var (result, executed) = await RunAsync(Sequence(ActionStep(0, "first"), GuardedLoop(1, guard: null)));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("first", "inner", "inner", "inner");

    var loop = LoopEntry(result);
    loop.ConditionType.Should().BeNull();
    loop.ConditionResult.Should().BeNull();
  }

  [Fact]
  public async Task GuardEvaluationErrorFailsSequence() {
    var guard = new ImageVisibleStepCondition { ImageId = "img" };

    var (result, executed) = await RunAsync(
        Sequence(GuardedLoop(0, guard), ActionStep(1, "after")),
        conditionEvaluator: (_, _) => throw new InvalidOperationException("eval error"));

    result.Status.Should().Be("Failed");
    executed.Should().BeEmpty();

    var loop = LoopEntry(result);
    loop.Status.Should().Be("Failed");
    loop.LoopIterations.Should().BeEmpty();
    loop.ConditionType.Should().Be("imageVisible");
    loop.ConditionResult.Should().Be("error");
  }

  [Fact]
  public async Task FalseGuardOnZeroCountLoopIsSkipped() {
    var (result, executed) = await RunAsync(Sequence(
        ActionStep(0, "first"),
        GuardedLoop(1, OutcomeOfFirst("failed"), new CountLoopConfig { Count = 0 })));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("first");
    LoopEntry(result).Status.Should().Be("Skipped");
    LoopEntry(result).ConditionResult.Should().Be("false");
  }

  [Fact]
  public async Task LaterCommandOutcomeReadsSkippedForSkippedLoop() {
    var (result, executed) = await RunAsync(Sequence(
        ActionStep(0, "first"),
        GuardedLoop(1, OutcomeOfFirst("failed")),
        ActionStep(2, "after", new CommandOutcomeStepCondition { StepRef = "loop", ExpectedState = "skipped" })));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("first", "after");
  }

  [Fact]
  public async Task FalseGuardSkipsWhileLoopBody() {
    var whileLoop = new WhileLoopConfig {
      Condition = new ImageVisibleStepCondition { ImageId = "img" },
      MaxIterations = 2
    };

    var (result, executed) = await RunAsync(
        Sequence(ActionStep(0, "first"), GuardedLoop(1, OutcomeOfFirst("failed"), whileLoop)),
        conditionEvaluator: (_, _) => Task.FromResult(true));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("first");
    LoopEntry(result).Status.Should().Be("Skipped");
  }
}
