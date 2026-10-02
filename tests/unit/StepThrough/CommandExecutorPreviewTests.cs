using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.StepThrough;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

/// <summary>
/// The preview option of the command executor (feature 127, FR-015a): a step-through previews a command
/// step with an outside effect and does not run it. A null option keeps the behavior of a real run.
/// </summary>
public sealed class CommandExecutorPreviewTests {
  private sealed class FakeCommandRepository : ICommandRepository {
    private readonly Dictionary<string, Command> _cmds = new(StringComparer.Ordinal);

    public void Seed(Command c) => _cmds[c.Id] = c;

    public Task<Command> AddAsync(Command c, CancellationToken ct = default) { _cmds[c.Id] = c; return Task.FromResult(c); }

    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_cmds.TryGetValue(id, out var c) ? c : null);

    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Command>)_cmds.Values.ToList());

    public Task<Command?> UpdateAsync(Command c, CancellationToken ct = default) { _cmds[c.Id] = c; return Task.FromResult<Command?>(c); }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_cmds.Remove(id));
  }

  private sealed class RecordingSessionManager : ISessionManager {
    private readonly EmulatorSession _session;

    public List<InputAction> ReceivedInputs { get; } = new();

    public RecordingSessionManager(EmulatorSession session) => _session = session;

    public int ActiveCount => 1;

    public bool CanCreateSession => false;

    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;

    public EmulatorSession CreateSession(string g, string? s = null) => throw new NotSupportedException();

    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };

    public bool StopSession(string id) => false;

    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) {
      ReceivedInputs.AddRange(actions);
      return Task.FromResult(1);
    }

    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default)
      => Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));

    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private sealed class FakeTriggerRepository : ITriggerRepository {
    public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Trigger?>(null);

    public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Trigger>)Array.Empty<Trigger>());

    public Task UpsertAsync(Trigger t, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
  }

  private sealed class FakeSessionContextCache : ISessionContextCache {
    public void SetSessionId(string g, string a, string s) { }

    public string? GetSessionId(string g, string a) => null;

    public void ClearSession(string g, string a) { }
  }

  private static EmulatorSession RunningSession() =>
    new() { Id = "session1", GameId = "queue:q1", Status = SessionStatus.Running, DeviceSerial = "emulator-5554" };

  private static Command KeyCommand() => new() {
    Id = "cmd1",
    Name = "Key",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() { Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = "Enter" }, Order = 1 }
    }
  };

  private static (CommandExecutor Executor, RecordingSessionManager Sessions) Build(Func<CommandStep, string?>? rule = null) {
    var cmds = new FakeCommandRepository();
    cmds.Seed(KeyCommand());
    var sessions = new RecordingSessionManager(RunningSession());
    var executor = new CommandExecutor(
      cmds,
      sessions,
      new FakeTriggerRepository(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new FakeSessionContextCache(),
      previewRule: rule);
    return (executor, sessions);
  }

  [Fact]
  public async Task NullOptionsKeepTheBehaviorOfARealRun() {
    var (executor, sessions) = Build(_ => "would send alert");

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1", new ExecutionLogContext(), ParameterScope.Empty, null);

    sessions.ReceivedInputs.Should().ContainSingle();
    result.PreviewedEffects.Should().BeNull();
    result.StepOutcomes.Single().Status.Should().Be("executed");
  }

  [Fact]
  public async Task PreviewModeStillRunsAStepThatOnlyUsesTheDevice() {
    var (executor, sessions) = Build();

    var result = await executor.ForceExecuteDetailedAsync(
      "session1", "cmd1", new ExecutionLogContext(), ParameterScope.Empty, new ExecutionOptions(PreviewEffects: true));

    sessions.ReceivedInputs.Should().ContainSingle();
    result.PreviewedEffects.Should().BeEmpty();
    result.Accepted.Should().Be(1);
  }

  [Fact]
  public async Task PreviewModeSkipsAStepWithAnOutsideEffectAndListsTheEffect() {
    var (executor, sessions) = Build(_ => "would send alert");

    var result = await executor.ForceExecuteDetailedAsync(
      "session1", "cmd1", new ExecutionLogContext(), ParameterScope.Empty, new ExecutionOptions(PreviewEffects: true));

    sessions.ReceivedInputs.Should().BeEmpty();
    result.PreviewedEffects.Should().Equal("would send alert");
    var outcome = result.StepOutcomes.Single();
    outcome.Status.Should().Be(PreviewEffectRules.PreviewedStatus);
    outcome.Reason.Should().Be("would send alert");
  }

  [Fact]
  public async Task AStepWithAnOutsideEffectStillRunsInARealRun() {
    var (executor, sessions) = Build(_ => "would send alert");

    var result = await executor.ForceExecuteDetailedAsync(
      "session1", "cmd1", new ExecutionLogContext(), ParameterScope.Empty, new ExecutionOptions(PreviewEffects: false));

    sessions.ReceivedInputs.Should().ContainSingle();
    result.PreviewedEffects.Should().BeNull();
  }

  [Fact]
  public async Task TheOldOverloadWithoutOptionsStillWorks() {
    var (executor, sessions) = Build();

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1", new ExecutionLogContext(), ParameterScope.Empty);

    sessions.ReceivedInputs.Should().ContainSingle();
    result.PreviewedEffects.Should().BeNull();
  }

  [Fact]
  public void EveryCommandStepTypeIsClassifiedInTheRuleTable() {
    foreach (var type in Enum.GetValues<CommandStepType>()) {
      PreviewEffectRules.ByType.Should().ContainKey(type, $"a new command step type '{type}' must be classified as runs or previews");
    }
  }

  [Fact]
  public void NoCommandStepTypeHasAnOutsideEffectToday() {
    foreach (var type in Enum.GetValues<CommandStepType>()) {
      PreviewEffectRules.TryDescribe(new CommandStep { Type = type }, out _).Should().BeFalse(type.ToString());
    }
  }
}
