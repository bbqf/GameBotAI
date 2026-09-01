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

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// A command step that dispatched no input to the device must stop reporting <c>executed</c>.
/// It reports <c>not_executed</c> instead, and fails the step only when the author opted in with
/// <see cref="SequenceStep.RequireDispatch"/> — because "tap it if it is there" is a deliberate
/// pattern that loops draining a list, and retry loops, depend on.
/// </summary>
public sealed class SequenceRunnerCommandDispatchOutcomeTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceStep CommandStep(string id, bool requireDispatch = false) => new() {
    StepId = id,
    CommandId = id,
    StepType = SequenceStepType.Action,
    RequireDispatch = requireDispatch
  };

  private static SequenceRunner RunnerFor(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    sequence.SetSteps(steps.Select((s, i) => { s.Order = i; return s; }).ToArray());
    sequence.InterStepDelayRangeMs = new DelayRangeMs { Min = 0, Max = 0 };
    return new SequenceRunner(new StubRepo(sequence));
  }

  private static Func<string, ParameterScope, Task<CommandDispatchOutcome>> Dispatcher(
      params (string Command, bool Dispatched)[] outcomes) {
    var map = outcomes.ToDictionary(o => o.Command, o => o.Dispatched, StringComparer.Ordinal);
    return (commandId, _) => Task.FromResult(
      map.TryGetValue(commandId, out var ok) && !ok
        ? new CommandDispatchOutcome(false, "detection_failed_after_3_retries")
        : CommandDispatchOutcome.Executed);
  }

  [Fact]
  public async Task DispatchedCommandStillReportsExecuted() {
    var runner = RunnerFor(CommandStep("a"));

    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
        commandDispatcher: Dispatcher(("a", true)), ct: CancellationToken.None);

    res.Status.Should().Be("Succeeded");
    res.Steps.Single().ActionOutcome.Should().Be("executed");
  }

  [Fact] // The reported bug: a missed anchored tap used to be indistinguishable from a real one.
  public async Task CommandThatDispatchedNothingReportsNotExecutedInsteadOfExecuted() {
    var runner = RunnerFor(CommandStep("a"));

    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
        commandDispatcher: Dispatcher(("a", false)), ct: CancellationToken.None);

    res.Steps.Single().ActionOutcome.Should().Be("not_executed");
    res.Steps.Single().Message.Should().Contain("detection_failed_after_3_retries");
  }

  [Fact] // Default stays lenient: the sequence carries on and still succeeds.
  public async Task WithoutRequireDispatchAMissDoesNotFailTheSequence() {
    var runner = RunnerFor(CommandStep("a"), CommandStep("b"));
    var ran = new List<string>();

    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
        commandDispatcher: (id, _) => {
          ran.Add(id);
          return Task.FromResult(id == "a"
            ? new CommandDispatchOutcome(false, "detection_failed_after_3_retries")
            : CommandDispatchOutcome.Executed);
        },
        ct: CancellationToken.None);

    res.Status.Should().Be("Succeeded");
    ran.Should().Equal("a", "b"); // the miss did not stop the run
  }

  [Fact] // Opt-in strict: the guard case — a missed tap must abort rather than carry on blindly.
  public async Task RequireDispatchFailsTheStepAndStopsTheSequence() {
    var runner = RunnerFor(CommandStep("a", requireDispatch: true), CommandStep("b"));
    var ran = new List<string>();

    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
        commandDispatcher: (id, _) => {
          ran.Add(id);
          return Task.FromResult(id == "a"
            ? new CommandDispatchOutcome(false, "detection_failed_after_3_retries")
            : CommandDispatchOutcome.Executed);
        },
        ct: CancellationToken.None);

    res.Status.Should().Be("Failed");
    res.Steps.Single().ActionOutcome.Should().Be("not_executed");
    ran.Should().Equal("a"); // "b" never ran — no acting on the wrong screen
  }

  [Fact] // RequireDispatch is irrelevant while the command keeps dispatching.
  public async Task RequireDispatchIsANoOpWhenTheCommandDispatches() {
    var runner = RunnerFor(CommandStep("a", requireDispatch: true), CommandStep("b"));

    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
        commandDispatcher: Dispatcher(("a", true), ("b", true)), ct: CancellationToken.None);

    res.Status.Should().Be("Succeeded");
    res.Steps.Should().HaveCount(2);
  }

  [Fact] // Callers that supply no dispatcher (every existing one) keep the old contract exactly.
  public async Task WithoutADispatcherBehaviourIsUnchanged() {
    var runner = RunnerFor(CommandStep("a", requireDispatch: true));
    var ran = 0;

    var res = await runner.ExecuteAsync("seq", (_, _) => { ran++; return Task.CompletedTask; },
        ct: CancellationToken.None);

    ran.Should().Be(1);
    res.Status.Should().Be("Succeeded");
    res.Steps.Single().ActionOutcome.Should().Be("executed");
  }

  [Fact] // The drain-until-empty loop: the terminating iteration misses, and that must be survivable.
  public async Task ALoopThatDrainsUntilNothingMatchesStillCompletes() {
    var loop = new SequenceStep {
      StepId = "drain",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 3 },
      Body = new[] { CommandStep("claim") }
    };
    var runner = RunnerFor(loop);
    var calls = 0;

    var res = await runner.ExecuteAsync("seq", (_, _) => Task.CompletedTask,
        commandDispatcher: (_, _) => {
          calls++;
          // Two items to claim, then nothing left to tap.
          return Task.FromResult(calls <= 2
            ? CommandDispatchOutcome.Executed
            : new CommandDispatchOutcome(false, "detection_failed_after_3_retries"));
        },
        ct: CancellationToken.None);

    res.Status.Should().Be("Succeeded");
    calls.Should().Be(3); // the loop ran to completion instead of aborting on the empty iteration
  }
}
