using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using DomainInputAction = GameBot.Domain.Actions.InputAction;

namespace GameBot.UnitTests.Commands;

public sealed class CommandExecutorTests {
  [Fact]
  public async Task EvaluateAndExecuteAsyncSatisfiedTriggerExecutesAndPersistsBeforeSendingInputs() {
    var trigger = CreateTrigger();
    var triggerRepo = new TriggerRepositorySpy(trigger);
    var command = CreateCommand(trigger.Id);
    var action = CreateAction(command.Steps[0].TargetId);
    var executor = CreateExecutor(command, action, triggerRepo, TriggerStatus.Satisfied, "delay_elapsed", out var sessionManager);

    var decision = await executor.EvaluateAndExecuteAsync(sessionManager.Session.Id, command.Id, CancellationToken.None).ConfigureAwait(false);

    decision.Accepted.Should().Be(1);
    decision.TriggerStatus.Should().Be(TriggerStatus.Satisfied);
    triggerRepo.UpsertCount.Should().Be(1);
    triggerRepo.LastUpsertedTrigger.Should().NotBeNull();
    triggerRepo.LastUpsertedTrigger!.LastFiredAt.Should().Be(triggerRepo.LastUpsertedTrigger.LastEvaluatedAt);
    sessionManager.SendInputsCalls.Should().Be(1);
    sessionManager.LastInputsAccepted.Should().Be(1);
    triggerRepo.SendInputsObservedUpsertCount.Should().Be(triggerRepo.UpsertCount);
  }

  [Fact]
  public async Task EvaluateAndExecuteAsyncPendingTriggerSkipsExecutionAndPreservesLastFiredAt() {
    var trigger = CreateTrigger();
    trigger.LastFiredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
    var triggerRepo = new TriggerRepositorySpy(trigger);
    var command = CreateCommand(trigger.Id);
    var action = CreateAction(command.Steps[0].TargetId);
    var executor = CreateExecutor(command, action, triggerRepo, TriggerStatus.Pending, "delay_pending", out var sessionManager);

    var decision = await executor.EvaluateAndExecuteAsync(sessionManager.Session.Id, command.Id, CancellationToken.None).ConfigureAwait(false);

    decision.Accepted.Should().Be(0);
    decision.TriggerStatus.Should().Be(TriggerStatus.Pending);
    decision.Reason.Should().Be("delay_pending");
    triggerRepo.UpsertCount.Should().Be(1);
    triggerRepo.LastUpsertedTrigger!.LastFiredAt.Should().Be(trigger.LastFiredAt);
    sessionManager.SendInputsCalls.Should().Be(0);
    triggerRepo.LastUpsertedTrigger.LastEvaluatedAt.Should().NotBeNull();
    triggerRepo.SendInputsObservedUpsertCount.Should().Be(0);
  }

  [Fact]
  public async Task EvaluateAndExecuteAsyncWithoutTriggerDoesNotExecuteAndReturnsPending() {
    var triggerRepo = new TriggerRepositorySpy(new Trigger { Id = "trig-unused", Type = TriggerType.Delay, Enabled = true, Params = new DelayParams { Seconds = 0 } });
    var command = new Command {
      Id = "cmd-no-trigger",
      Name = "NoTriggerCommand",
      TriggerId = null,
      Steps = new Collection<CommandStep> { new() { Type = CommandStepType.Action, TargetId = "act-1", Order = 1 } }
    };
    var action = CreateAction(command.Steps[0].TargetId);
    var commandRepo = new CommandRepositoryStub(command);
    var actionRepo = new ActionRepositoryStub(action);
    var sessionManager = new SessionManagerStub(triggerRepo);
    var triggerService = new TriggerEvaluationService(new[] { new StaticResultEvaluator(TriggerStatus.Satisfied, "should_not_matter") });
    var executor = new CommandExecutor(commandRepo, actionRepo, sessionManager, triggerRepo, triggerService, NullLogger<CommandExecutor>.Instance);

    var decision = await executor.EvaluateAndExecuteAsync(sessionManager.Session.Id, command.Id, CancellationToken.None).ConfigureAwait(false);

    decision.Accepted.Should().Be(0);
    decision.TriggerStatus.Should().Be(TriggerStatus.Pending);
    decision.Reason.Should().Be("no_trigger_configured");
    sessionManager.SendInputsCalls.Should().Be(0);
    triggerRepo.UpsertCount.Should().Be(0);
  }

  private static Command CreateCommand(string triggerId) => new() {
    Id = "cmd-1",
    Name = "TestCommand",
    TriggerId = triggerId,
    Steps = new Collection<CommandStep> {
      new() { Type = CommandStepType.Action, TargetId = "act-1", Order = 1 }
    }
  };

  private static GameBot.Domain.Actions.Action CreateAction(string actionId) {
    var action = new GameBot.Domain.Actions.Action {
      Id = actionId,
      Name = "Action1",
      GameId = "game-1"
    };
    action.Steps.Add(new DomainInputAction { Type = "tap", Args = new Dictionary<string, object> { ["x"] = 1, ["y"] = 1 } });
    return action;
  }

  private static Trigger CreateTrigger() => new() {
    Id = "trig-1",
    Type = TriggerType.Delay,
    Enabled = true,
    CooldownSeconds = 0,
    Params = new DelayParams { Seconds = 0 }
  };

  private static CommandExecutor CreateExecutor(Command command, GameBot.Domain.Actions.Action action, TriggerRepositorySpy triggerRepo, TriggerStatus status, string? reason, out SessionManagerStub sessionManager) {
    var commandRepo = new CommandRepositoryStub(command);
    var actionRepo = new ActionRepositoryStub(action);
    sessionManager = new SessionManagerStub(triggerRepo);
    var evaluator = new StaticResultEvaluator(status, reason);
    var triggerService = new TriggerEvaluationService(new[] { evaluator });
    return new CommandExecutor(commandRepo, actionRepo, sessionManager, triggerRepo, triggerService, NullLogger<CommandExecutor>.Instance);
  }

  private sealed class CommandRepositoryStub : ICommandRepository {
    private readonly Command _command;
    public CommandRepositoryStub(Command command) => _command = command;
    public Task<Command> AddAsync(Command command, CancellationToken ct = default) => Task.FromResult(command);
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Command?>(id == _command.Id ? _command : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Command>>(new[] { _command });
    public Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) => Task.FromResult<Command?>(command);
  }

  private sealed class ActionRepositoryStub : IActionRepository {
    private readonly GameBot.Domain.Actions.Action _action;
    public ActionRepositoryStub(GameBot.Domain.Actions.Action action) => _action = action;
    public Task<GameBot.Domain.Actions.Action> AddAsync(GameBot.Domain.Actions.Action action, CancellationToken ct = default) => Task.FromResult(action);
    public Task<GameBot.Domain.Actions.Action?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameBot.Domain.Actions.Action?>(id == _action.Id ? _action : null);
    public Task<IReadOnlyList<GameBot.Domain.Actions.Action>> ListAsync(string? gameId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GameBot.Domain.Actions.Action>>(new[] { _action });
    public Task<GameBot.Domain.Actions.Action?> UpdateAsync(GameBot.Domain.Actions.Action action, CancellationToken ct = default) => Task.FromResult<GameBot.Domain.Actions.Action?>(action);
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
  }

  private sealed class TriggerRepositorySpy : ITriggerRepository {
    private readonly Trigger _trigger;
    public TriggerRepositorySpy(Trigger trigger) => _trigger = trigger;
    public int UpsertCount { get; private set; }
    public int SendInputsObservedUpsertCount { get; private set; }
    public Trigger? LastUpsertedTrigger { get; private set; }
    public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Trigger?>(id == _trigger.Id ? _trigger : null);
    public Task UpsertAsync(Trigger trigger, CancellationToken ct = default) {
      UpsertCount++;
      LastUpsertedTrigger = trigger;
      return Task.CompletedTask;
    }
    public void NotifyInputsSent() => SendInputsObservedUpsertCount = UpsertCount;
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
    public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trigger>>(new[] { _trigger });
  }

  private sealed class SessionManagerStub : ISessionManager {
    private readonly TriggerRepositorySpy _triggerRepo;
    public SessionManagerStub(TriggerRepositorySpy triggerRepo) {
      _triggerRepo = triggerRepo;
      Session = new EmulatorSession { Id = "sess-1", GameId = "game-1", Status = SessionStatus.Running };
    }

    public EmulatorSession Session { get; }
    public int SendInputsCalls { get; private set; }
    public int LastInputsAccepted { get; private set; }

    public int ActiveCount => 1;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
    public EmulatorSession? GetSession(string id) => id == Session.Id ? Session : null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { Session };
    public bool StopSession(string id) => true;
    public Task<int> SendInputsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) {
      SendInputsCalls++;
      var accepted = actions.Count();
      LastInputsAccepted = accepted;
      _triggerRepo.NotifyInputsSent();
      return Task.FromResult(accepted);
    }
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private sealed class StaticResultEvaluator : ITriggerEvaluator {
    private readonly TriggerStatus _status;
    private readonly string? _reason;
    public StaticResultEvaluator(TriggerStatus status, string? reason) {
      _status = status;
      _reason = reason;
    }
    public bool CanEvaluate(Trigger trigger) => true;
    public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now) => new() { Status = _status, Reason = _reason, EvaluatedAt = now };
  }
}
