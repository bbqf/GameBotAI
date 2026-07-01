using System;
using System.Collections.Generic;
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
/// Feature 065: a <c>reschedule-self</c> action step is dispatched through the injected callback,
/// its outcome is recorded as the step result, and the sequence continues (FR-012). The dispatch
/// path performs no command/ADB I/O (SC-006).
/// </summary>
public sealed class SequenceRunnerActionDispatchTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static CommandSequence BuildSequence() {
    var sequence = new CommandSequence { Id = "reschedule-seq", Name = "Reschedule Seq" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "reschedule",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload {
          Type = ActionTypes.RescheduleSelf,
          Parameters = { ["option"] = "OncePerRun" }
        }
      },
      new SequenceStep {
        Order = 1,
        StepId = "after",
        CommandId = "cmd-after",
        StepType = SequenceStepType.Command
      }
    });
    return sequence;
  }

  [Fact]
  public async Task DispatchesRescheduleActionRecordsOutcomeAndContinues() {
    var executedCommands = new List<string>();
    var dispatchedActions = new List<SequenceActionPayload>();
    var runner = new SequenceRunner(new StubRepo(BuildSequence()));

    var result = await runner.ExecuteAsync(
      "reschedule-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (action, _) => {
        dispatchedActions.Add(action);
        return Task.FromResult(new ActionDispatchResult("scheduled", "rescheduled (OncePerRun)"));
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    // The dispatcher was invoked once with the reschedule-self payload.
    dispatchedActions.Should().ContainSingle();
    dispatchedActions[0].Type.Should().Be(ActionTypes.RescheduleSelf);
    // The action recorded a step with the returned outcome.
    result.Steps.Should().Contain(s => s.ActionOutcome == "scheduled" && s.Message == "rescheduled (OncePerRun)");
    // The sequence continued: the following command step still ran...
    executedCommands.Should().Contain("cmd-after");
    // ...and the dispatch path performed NO command I/O for the action itself (SC-006).
    executedCommands.Should().NotContain(string.Empty);
    executedCommands.Should().NotContain("reschedule");
  }

  [Fact] // T044 — a no-op dispatch result (e.g. not started from a queue) is success + non-terminating.
  public async Task NoOpDispatchResultIsRecordedAsSuccessAndContinues() {
    var executedCommands = new List<string>();
    var runner = new SequenceRunner(new StubRepo(BuildSequence()));

    var result = await runner.ExecuteAsync(
      "reschedule-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (_, _) => Task.FromResult(new ActionDispatchResult("noop", "no originating queue, no reschedule performed")),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    result.Steps.Should().Contain(s => s.ActionOutcome == "noop");
    executedCommands.Should().Contain("cmd-after"); // sequence continued
  }

  [Fact]
  public async Task WithoutDispatcherRescheduleActionIsANonTerminatingNoOp() {
    var executedCommands = new List<string>();
    var runner = new SequenceRunner(new StubRepo(BuildSequence()));

    // No dispatcher supplied (e.g. a path that doesn't wire one): must not throw or early-stop.
    var result = await runner.ExecuteAsync(
      "reschedule-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executedCommands.Should().Contain("cmd-after");
  }
}
