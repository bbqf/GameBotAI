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
/// Primitive input action steps (tap/swipe/key) are dispatched through the injected callback
/// to the session input pipeline instead of falling through to the command path, where a
/// missing command was previously swallowed and the step reported a silent fake success.
/// A failed dispatch fails the step and stops the sequence.
/// </summary>
public sealed class SequenceRunnerPrimitiveInputDispatchTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceStep PrimitiveStep(int order, string stepId, string actionType, params (string Key, object Value)[] parameters) {
    var payload = new SequenceActionPayload { Type = actionType };
    foreach (var (key, value) in parameters) payload.Parameters[key] = value;
    return new SequenceStep {
      Order = order,
      StepId = stepId,
      // The API maps primitive steps with CommandId = StepId; replicate that so the tests
      // prove the dispatcher wins over the (dangling) command fallback.
      CommandId = stepId,
      StepType = SequenceStepType.Action,
      Action = payload
    };
  }

  private static CommandSequence Sequence(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "primitive-seq", Name = "Primitive Seq" };
    sequence.SetSteps(steps);
    return sequence;
  }

  [Fact]
  public async Task TapStepIsDispatchedAndRecordedWithDispatchOutcome() {
    var executedCommands = new List<string>();
    var dispatched = new List<SequenceActionPayload>();
    var sequence = Sequence(
      PrimitiveStep(0, "tap-step", ActionTypes.Tap, ("x", 100), ("y", 200)),
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command });
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "primitive-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (action, _) => {
        dispatched.Add(action);
        return Task.FromResult(new ActionDispatchResult("executed", "tap(100,200) sent to emulator"));
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    dispatched.Should().ContainSingle();
    dispatched[0].Type.Should().Be(ActionTypes.Tap);
    dispatched[0].Parameters["x"].Should().Be(100);
    result.Steps.Should().Contain(s => s.CommandId == "tap-step" && s.ActionOutcome == "executed" && s.Message == "tap(100,200) sent to emulator");
    // The tap step performed no command I/O; the following command step still ran.
    executedCommands.Should().Equal("cmd-after");
  }

  [Fact]
  public async Task FailedDispatchFailsTheStepAndStopsTheSequence() {
    var executedCommands = new List<string>();
    var sequence = Sequence(
      PrimitiveStep(0, "key-step", ActionTypes.Key, ("key", "BACK")),
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command });
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "primitive-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (_, _) => Task.FromResult(new ActionDispatchResult("failed", "no session available for 'key' step; start a session or pass a sessionId")),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    result.Steps.Should().Contain(s => s.CommandId == "key-step" && s.Status == "Failed" && s.ActionOutcome == "failed");
    executedCommands.Should().BeEmpty(); // sequence stopped before the next step
  }

  [Fact]
  public async Task PrimitiveStepInsideLoopBodyIsDispatchedPerIteration() {
    var dispatched = new List<SequenceActionPayload>();
    var loopStep = new SequenceStep {
      Order = 0,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 3 },
      Body = new List<SequenceStep> { PrimitiveStep(0, "tap-in-loop", ActionTypes.Tap, ("x", 10), ("y", 20)) }
    };
    var runner = new SequenceRunner(new StubRepo(Sequence(loopStep)));

    var result = await runner.ExecuteAsync(
      "primitive-seq",
      _ => Task.CompletedTask,
      actionDispatcher: (action, _) => {
        dispatched.Add(action);
        return Task.FromResult(new ActionDispatchResult("executed", null));
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    dispatched.Should().HaveCount(3);
  }

  [Fact]
  public async Task WithoutDispatcherPrimitiveStepFallsThroughToCommandPath() {
    // Callers that supply no dispatcher (unit-test harnesses) keep the legacy behavior of
    // executing the step through executeCommandAsync.
    var executedCommands = new List<string>();
    var runner = new SequenceRunner(new StubRepo(Sequence(PrimitiveStep(0, "tap-step", ActionTypes.Tap, ("x", 1), ("y", 2)))));

    var result = await runner.ExecuteAsync(
      "primitive-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executedCommands.Should().Equal("tap-step");
  }
}
