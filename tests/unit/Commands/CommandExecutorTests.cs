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
    var executor = CreateExecutor(command, triggerRepo, TriggerStatus.Satisfied, "delay_elapsed", out var sessionManager);

    var decision = await executor.EvaluateAndExecuteAsync(sessionManager.Session.Id, command.Id, CancellationToken.None).ConfigureAwait(false);

    decision.Accepted.Should().Be(0);
    decision.TriggerStatus.Should().Be(TriggerStatus.Satisfied);
    triggerRepo.UpsertCount.Should().Be(1);
    triggerRepo.LastUpsertedTrigger.Should().NotBeNull();
    triggerRepo.LastUpsertedTrigger!.LastFiredAt.Should().Be(triggerRepo.LastUpsertedTrigger.LastEvaluatedAt);
    sessionManager.SendInputsCalls.Should().Be(0);
    sessionManager.LastInputsAccepted.Should().Be(0);
    triggerRepo.SendInputsObservedUpsertCount.Should().Be(0);
  }

  [Fact]
  public async Task EvaluateAndExecuteAsyncPendingTriggerSkipsExecutionAndPreservesLastFiredAt() {
    var trigger = CreateTrigger();
    trigger.LastFiredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
    var triggerRepo = new TriggerRepositorySpy(trigger);
    var command = CreateCommand(trigger.Id);
    var executor = CreateExecutor(command, triggerRepo, TriggerStatus.Pending, "delay_pending", out var sessionManager);

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
      Steps = new Collection<CommandStep> { new() { Type = CommandStepType.PrimitiveTap, PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("template-1") }, Order = 1 } }
    };
    var commandRepo = new CommandRepositoryStub(command);
    var sessionManager = new SessionManagerStub(triggerRepo);
    var triggerService = new TriggerEvaluationService(new[] { new StaticResultEvaluator(TriggerStatus.Satisfied, "should_not_matter") });
    var executor = new CommandExecutor(commandRepo, sessionManager, triggerRepo, triggerService, NullLogger<CommandExecutor>.Instance, new SessionContextCache());

    var decision = await executor.EvaluateAndExecuteAsync(sessionManager.Session.Id, command.Id, CancellationToken.None).ConfigureAwait(false);

    decision.Accepted.Should().Be(0);
    decision.TriggerStatus.Should().Be(TriggerStatus.Pending);
    decision.Reason.Should().Be("no_trigger_configured");
    sessionManager.SendInputsCalls.Should().Be(0);
    triggerRepo.UpsertCount.Should().Be(0);
  }

  [Fact]
  public async Task ForceExecuteDetailedAsyncActionOnlyCommandKeepsLegacyBehaviorAndNoPrimitiveOutcomes() {
    var trigger = CreateTrigger();
    var triggerRepo = new TriggerRepositorySpy(trigger);
    var command = new Command {
      Id = "cmd-action-only",
      Name = "ActionOnly",
      TriggerId = null,
      Steps = new Collection<CommandStep> { new() { Type = CommandStepType.PrimitiveTap, PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("template-1") }, Order = 0 } }
    };
    var commandRepo = new CommandRepositoryStub(command);
    var sessionManager = new SessionManagerStub(triggerRepo);
    var triggerService = new TriggerEvaluationService(new[] { new StaticResultEvaluator(TriggerStatus.Satisfied, "unused") });
    var executor = new CommandExecutor(commandRepo, sessionManager, triggerRepo, triggerService, NullLogger<CommandExecutor>.Instance, new SessionContextCache());

    var result = await executor.ForceExecuteDetailedAsync(sessionManager.Session.Id, command.Id, CancellationToken.None).ConfigureAwait(false);

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("skipped_invalid_config");
    result.StepOutcomes[0].Reason.Should().Be("services_unavailable");
    sessionManager.SendInputsCalls.Should().Be(0);
  }

  private static Command CreateCommand(string triggerId) => new() {
    Id = "cmd-1",
    Name = "TestCommand",
    TriggerId = triggerId,
    Steps = new Collection<CommandStep> {
      new() { Type = CommandStepType.PrimitiveTap, PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("template-1") }, Order = 1 }
    }
  };

  private static PrimitiveTapConfig CreatePrimitiveTapConfig() => new() {
    DetectionTarget = new DetectionTarget("template-1")
  };

  private static Trigger CreateTrigger() => new() {
    Id = "trig-1",
    Type = TriggerType.Delay,
    Enabled = true,
    CooldownSeconds = 0,
    Params = new DelayParams { Seconds = 0 }
  };

  private static CommandExecutor CreateExecutor(Command command, TriggerRepositorySpy triggerRepo, TriggerStatus status, string? reason, out SessionManagerStub sessionManager) {
    var commandRepo = new CommandRepositoryStub(command);
    sessionManager = new SessionManagerStub(triggerRepo);
    var evaluator = new StaticResultEvaluator(status, reason);
    var triggerService = new TriggerEvaluationService(new[] { evaluator });
    return new CommandExecutor(commandRepo, sessionManager, triggerRepo, triggerService, NullLogger<CommandExecutor>.Instance, new SessionContextCache());
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
