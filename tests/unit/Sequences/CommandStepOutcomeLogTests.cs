using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 167: each evaluation of a commandStepOutcome condition writes one log line, a missing outcome
/// is false and does not fail the run, and the latest command run of a step counts.
/// </summary>
public sealed class CommandStepOutcomeLogTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceStep CommandStep(string id, SequenceStepCondition? condition = null) => new() {
    StepId = id,
    CommandId = id,
    StepType = SequenceStepType.Action,
    Condition = condition
  };

  private static CommandStepOutcomeStepCondition Condition(string expected, int order = 12, string stepRef = "send-1", bool negate = false) => new() {
    StepRef = stepRef,
    CommandStepOrder = order,
    ExpectedState = expected,
    Negate = negate
  };

  private static SequenceRunner RunnerFor(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    sequence.SetSteps(steps.Select((s, i) => { s.Order = i; return s; }).ToArray());
    sequence.InterStepDelayRangeMs = new DelayRangeMs { Min = 0, Max = 0 };
    return new SequenceRunner(new StubRepo(sequence));
  }

  private static CommandDispatchOutcome WithStates(params (int Order, string State)[] states) =>
    new(true, null, StepStates: states.ToDictionary(s => s.Order, s => s.State));

  private static Task<SequenceExecutionResult> RunAsync(
      SequenceRunner runner, Func<string, ParameterScope, Task<CommandDispatchOutcome>> dispatcher, bool dryRun = false) =>
    runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask, commandDispatcher: dispatcher, dryRun: dryRun, ct: CancellationToken.None);

  private static string OutcomeOf(SequenceExecutionResult result, int index) => result.Steps[index].ActionOutcome ?? string.Empty;

  [Theory]
  [InlineData("executed")]
  [InlineData("skipped_detection_failed")]
  [InlineData("completed_timeout")]
  [InlineData("image_detected")]
  public async Task EachOfTheFourStatesMatchesOnlyItself(string state) {
    foreach (var expected in CommandStepOutcomeStates.All) {
      var runner = RunnerFor(CommandStep("send-1"), CommandStep("next", Condition(expected)));

      var res = await RunAsync(runner, (id, _) => Task.FromResult(id == "send-1" ? WithStates((12, state)) : CommandDispatchOutcome.Executed));

      res.Status.Should().Be("Succeeded");
      var ran = res.Steps.Single(s => s.CommandId == "next").Status == "Succeeded";
      ran.Should().Be(expected == state, $"state {state} against expected {expected}");
    }
  }

  [Fact]
  public async Task TheLogLineShowsTheFieldsAndTheStateRead() {
    var runner = RunnerFor(CommandStep("send-1"), CommandStep("next", Condition("completed_timeout")));

    var res = await RunAsync(runner, (id, _) => Task.FromResult(id == "send-1" ? WithStates((12, "executed")) : CommandDispatchOutcome.Executed));

    var skipped = res.Steps.Single(s => s.CommandId == "next");
    skipped.Status.Should().Be("Skipped");
    skipped.Message.Should().Contain("commandStepOutcome(stepRef=send-1, order=12, expected=completed_timeout) actual=executed");
  }

  [Fact]
  public async Task NoOutcomeGivesNoneAndFalseWithoutFailingTheRun() {
    var runner = RunnerFor(CommandStep("send-1"), CommandStep("next", Condition("executed")));

    var res = await RunAsync(runner, (_, _) => Task.FromResult(CommandDispatchOutcome.Executed));

    res.Status.Should().Be("Succeeded");
    var skipped = res.Steps.Single(s => s.CommandId == "next");
    skipped.Status.Should().Be("Skipped");
    skipped.Message.Should().Contain("actual=none (no outcome recorded)");
  }

  [Fact]
  public async Task NegateOfAMissingOutcomeIsTrue() {
    var runner = RunnerFor(CommandStep("send-1"), CommandStep("next", Condition("executed", negate: true)));

    var res = await RunAsync(runner, (_, _) => Task.FromResult(CommandDispatchOutcome.Executed));

    res.Steps.Single(s => s.CommandId == "next").Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task AStepOrderThatWasNotReachedGivesNoOutcome() {
    var runner = RunnerFor(CommandStep("send-1"), CommandStep("next", Condition("executed", order: 20)));

    var res = await RunAsync(runner, (id, _) => Task.FromResult(id == "send-1" ? WithStates((12, "executed")) : CommandDispatchOutcome.Executed));

    res.Steps.Single(s => s.CommandId == "next").Message.Should().Contain("actual=none (no outcome recorded)");
  }

  [Fact]
  public async Task ASkippedSequenceStepGivesNoOutcome() {
    var never = new ImageVisibleStepCondition { ImageId = "never-visible" };
    var runner = RunnerFor(
      CommandStep("send-1", never),
      CommandStep("next", Condition("executed")));

    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
      commandDispatcher: (_, _) => Task.FromResult(WithStates((12, "executed"))),
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None);

    res.Steps.Single(s => s.CommandId == "next").Message.Should().Contain("actual=none (no outcome recorded)");
  }

  [Fact]
  public async Task ADryRunGivesNoOutcome() {
    var runner = RunnerFor(CommandStep("send-1"), CommandStep("next", Condition("executed")));

    var res = await RunAsync(
      runner,
      (_, _) => Task.FromResult(new CommandDispatchOutcome(false, null, SkippedDryRun: true)),
      dryRun: true);

    res.Steps.Single(s => s.CommandId == "next").Message.Should().Contain("actual=none (no outcome recorded)");
  }

  [Fact]
  public async Task TheLatestRunOfALoopStepCounts() {
    var calls = 0;
    var body = new List<SequenceStep> {
      CommandStep("send-1"),
      new() {
        StepId = "stop",
        StepType = SequenceStepType.Break,
        BreakCondition = Condition("completed_timeout")
      }
    };
    var loop = new SequenceStep {
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 5 },
      Body = body
    };
    var runner = RunnerFor(loop);

    var res = await RunAsync(runner, (_, _) => {
      calls++;
      return Task.FromResult(WithStates((12, calls < 3 ? "executed" : "completed_timeout")));
    });

    res.Status.Should().Be("Succeeded");
    calls.Should().Be(3);
  }

  [Fact]
  public void ANewRunWithNoReadableStatesReplacesTheOlderEntry() {
    var scope = new CommandStepStateScope();
    scope.Set("send-1", new Dictionary<int, string> { [12] = "executed" });

    scope.Set("send-1", null);

    scope.TryGetState("send-1", 12).Should().BeNull();
  }
}
