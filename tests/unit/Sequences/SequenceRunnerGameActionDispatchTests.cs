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
/// connect-to-game and ensure-game-running action steps are dispatched through the injected
/// callback instead of falling through to the command path, where the (dangling) command
/// reference was previously swallowed and the step reported a silent fake success.
/// A failed dispatch fails the step and stops the sequence.
/// </summary>
public sealed class SequenceRunnerGameActionDispatchTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceStep ActionStep(int order, string stepId, string actionType, params (string Key, object Value)[] parameters) {
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
    var sequence = new CommandSequence { Id = "game-action-seq", Name = "Game Action Seq" };
    sequence.SetSteps(steps);
    return sequence;
  }

  [Fact]
  public async Task EnsureGameRunningStepIsDispatchedAndRecordedWithDispatchOutcome() {
    var executedCommands = new List<string>();
    var dispatched = new List<SequenceActionPayload>();
    var sequence = Sequence(
      ActionStep(0, "ensure-step", ActionTypes.EnsureGameRunning),
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command });
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "game-action-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (action, _) => {
        dispatched.Add(action);
        return Task.FromResult(new ActionDispatchResult("executed", "game is running in the foreground (game_running)"));
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    dispatched.Should().ContainSingle();
    dispatched[0].Type.Should().Be(ActionTypes.EnsureGameRunning);
    result.Steps.Should().Contain(s => s.CommandId == "ensure-step" && s.ActionOutcome == "executed" && s.Message == "game is running in the foreground (game_running)");
    // The action step performed no command I/O; the following command step still ran.
    executedCommands.Should().Equal("cmd-after");
  }

  [Fact]
  public async Task ConnectToGameStepIsDispatchedAndRecordedWithDispatchOutcome() {
    var executedCommands = new List<string>();
    var dispatched = new List<SequenceActionPayload>();
    var sequence = Sequence(
      ActionStep(0, "connect-step", ActionTypes.ConnectToGame, ("gameId", "game-1"), ("adbSerial", "emulator-5554")),
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command });
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "game-action-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (action, _) => {
        dispatched.Add(action);
        return Task.FromResult(new ActionDispatchResult("executed", "connected to game 'game-1' on device 'emulator-5554' (session s-1)"));
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    dispatched.Should().ContainSingle();
    dispatched[0].Type.Should().Be(ActionTypes.ConnectToGame);
    dispatched[0].Parameters["gameId"].Should().Be("game-1");
    result.Steps.Should().Contain(s => s.CommandId == "connect-step" && s.ActionOutcome == "executed");
    executedCommands.Should().Equal("cmd-after");
  }

  [Fact]
  public async Task GoToHomeScreenStepIsDispatchedAndRecordedWithDispatchOutcome() {
    var executedCommands = new List<string>();
    var dispatched = new List<SequenceActionPayload>();
    var sequence = Sequence(
      ActionStep(0, "home-step", ActionTypes.GoToHomeScreen),
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command });
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "game-action-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (action, _) => {
        dispatched.Add(action);
        return Task.FromResult(new ActionDispatchResult("executed", "pressed HOME; device returned to the home screen (game left running)"));
      },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    dispatched.Should().ContainSingle();
    dispatched[0].Type.Should().Be(ActionTypes.GoToHomeScreen);
    result.Steps.Should().Contain(s => s.CommandId == "home-step" && s.ActionOutcome == "executed");
    // The action step performed no command I/O; the following command step still ran.
    executedCommands.Should().Equal("cmd-after");
  }

  [Fact]
  public async Task FailedGoToHomeScreenDispatchFailsTheStepAndStopsTheSequence() {
    var executedCommands = new List<string>();
    var sequence = Sequence(
      ActionStep(0, "home-step", ActionTypes.GoToHomeScreen),
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command });
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "game-action-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (_, _) => Task.FromResult(new ActionDispatchResult("failed", "no session available for 'go-to-home-screen' step; start a session or pass a sessionId")),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    result.Steps.Should().Contain(s => s.CommandId == "home-step" && s.Status == "Failed" && s.ActionOutcome == "failed");
    executedCommands.Should().BeEmpty();
  }

  [Fact]
  public async Task FailedEnsureGameRunningDispatchFailsTheStepAndStopsTheSequence() {
    var executedCommands = new List<string>();
    var sequence = Sequence(
      ActionStep(0, "ensure-step", ActionTypes.EnsureGameRunning),
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command });
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "game-action-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (_, _) => Task.FromResult(new ActionDispatchResult("failed", "ensure-game-running failed: game_not_running")),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    result.Steps.Should().Contain(s => s.CommandId == "ensure-step" && s.Status == "Failed" && s.ActionOutcome == "failed");
    executedCommands.Should().BeEmpty(); // sequence stopped before the next step
  }

  [Fact]
  public async Task FailedConnectToGameDispatchFailsTheStepAndStopsTheSequence() {
    var executedCommands = new List<string>();
    var sequence = Sequence(
      ActionStep(0, "connect-step", ActionTypes.ConnectToGame),
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command });
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "game-action-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      actionDispatcher: (_, _) => Task.FromResult(new ActionDispatchResult("failed", "connect-to-game step requires 'gameId' and 'adbSerial' parameters")),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    result.Steps.Should().Contain(s => s.CommandId == "connect-step" && s.Status == "Failed" && s.ActionOutcome == "failed");
    executedCommands.Should().BeEmpty();
  }

  [Fact]
  public async Task WithoutDispatcherGameActionStepFallsThroughToCommandPath() {
    // Callers that supply no dispatcher (unit-test harnesses) keep the legacy behavior of
    // executing the step through executeCommandAsync.
    var executedCommands = new List<string>();
    var runner = new SequenceRunner(new StubRepo(Sequence(ActionStep(0, "ensure-step", ActionTypes.EnsureGameRunning))));

    var result = await runner.ExecuteAsync(
      "game-action-seq",
      commandId => { executedCommands.Add(commandId); return Task.CompletedTask; },
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executedCommands.Should().Equal("ensure-step");
  }
}
