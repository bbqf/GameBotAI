using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Commands;

public sealed class CommandExecutorPrimitiveTapTests {
  [Fact]
  public async Task ForceExecuteDetailedAsyncReturnsSkippedInvalidConfigWhenDetectionServicesUnavailable() {
    var command = new Command {
      Id = "cmd-primitive",
      Name = "Primitive",
      TriggerId = null,
      Steps = new Collection<CommandStep> {
        new() {
          Type = CommandStepType.PrimitiveTap,
          TargetId = string.Empty,
          Order = 0,
          PrimitiveTap = new PrimitiveTapConfig {
            DetectionTarget = new DetectionTarget("img-1", 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
          }
        }
      }
    };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new ActionRepoStub(),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new SessionContextCache());

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].StepOrder.Should().Be(0);
    result.StepOutcomes[0].Status.Should().Be("skipped_invalid_config");
    result.StepOutcomes[0].Reason.Should().Be("services_unavailable");
  }

  private sealed class CommandRepoStub : ICommandRepository {
    private readonly Command _command;
    public CommandRepoStub(Command command) => _command = command;
    public Task<Command> AddAsync(Command command, CancellationToken ct = default) => Task.FromResult(command);
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Command?>(id == _command.Id ? _command : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Command>>(new[] { _command });
    public Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) => Task.FromResult<Command?>(command);
  }

  private sealed class ActionRepoStub : GameBot.Domain.Actions.IActionRepository {
    public Task<GameBot.Domain.Actions.Action> AddAsync(GameBot.Domain.Actions.Action action, CancellationToken ct = default) => Task.FromResult(action);
    public Task<GameBot.Domain.Actions.Action?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameBot.Domain.Actions.Action?>(null);
    public Task<IReadOnlyList<GameBot.Domain.Actions.Action>> ListAsync(string? gameId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GameBot.Domain.Actions.Action>>(Array.Empty<GameBot.Domain.Actions.Action>());
    public Task<GameBot.Domain.Actions.Action?> UpdateAsync(GameBot.Domain.Actions.Action action, CancellationToken ct = default) => Task.FromResult<GameBot.Domain.Actions.Action?>(action);
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
  }

  private sealed class TriggerRepoStub : ITriggerRepository {
    public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Trigger?>(null);
    public Task UpsertAsync(Trigger trigger, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
    public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trigger>>(Array.Empty<Trigger>());
  }

  private sealed class SessionManagerStub : ISessionManager {
    private readonly EmulatorSession _session = new() { Id = "sess-1", Status = SessionStatus.Running, GameId = "game-1" };
    public int ActiveCount => 1;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => _session;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => true;
    public Task<int> SendInputsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) => Task.FromResult(actions.Count());
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }
}
