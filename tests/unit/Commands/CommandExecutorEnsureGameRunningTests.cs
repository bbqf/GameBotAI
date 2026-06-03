using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using GameBot.Service.Services.EnsureGameRunning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Commands;

public sealed class CommandExecutorEnsureGameRunningTests {
  // ── Fakes ─────────────────────────────────────────────────────────────────

  private sealed class FakeCommandRepository : ICommandRepository {
    private readonly Dictionary<string, Command> _cmds = new(StringComparer.Ordinal);
    public void Seed(Command c) => _cmds[c.Id] = c;
    public Task<Command> AddAsync(Command c, CancellationToken ct = default) { _cmds[c.Id] = c; return Task.FromResult(c); }
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_cmds.TryGetValue(id, out var c) ? c : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Command>)_cmds.Values.ToList());
    public Task<Command?> UpdateAsync(Command c, CancellationToken ct = default) { _cmds[c.Id] = c; return Task.FromResult<Command?>(c); }
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_cmds.Remove(id));
  }

  private sealed class FakeSessionManager : ISessionManager {
    private readonly EmulatorSession _session;
    public FakeSessionManager(EmulatorSession session) => _session = session;
    public int ActiveCount => 1;
    public bool CanCreateSession => false;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public EmulatorSession CreateSession(string g, string? s = null) => throw new NotSupportedException();
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
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

  private sealed class StubHandler : IEnsureGameRunningActionHandler {
    private readonly EnsureGameRunningActionResult _result;
    public StubHandler(EnsureGameRunningActionResult result) => _result = result;
    public Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(_result);
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  private static EmulatorSession RunningSession(string id = "session1") =>
    new() { Id = id, GameId = "queue:q1", Status = SessionStatus.Running, DeviceSerial = "emulator-5554" };

  private static Command EnsureRunningCommand(string id = "cmd1") => new() {
    Id = id,
    Name = "EnsureGameRunning",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() { Type = CommandStepType.EnsureGameRunning, TargetId = string.Empty, Order = 1 }
    }
  };

  private static CommandExecutor BuildExecutor(IEnsureGameRunningActionHandler? handler, FakeCommandRepository cmds, ISessionManager sessions) =>
    new(cmds, sessions, new FakeTriggerRepository(),
        new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
        NullLogger<CommandExecutor>.Instance,
        new FakeSessionContextCache(),
        ensureGameRunning: handler);

  // ── Tests ─────────────────────────────────────────────────────────────────

  [Fact]
  public async Task EnsureGameRunningSuccessOutcomeReturnsAccepted1AndExecutedStatus() {
    var cmds = new FakeCommandRepository();
    cmds.Seed(EnsureRunningCommand());
    var executor = BuildExecutor(
      new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning)),
      cmds, new FakeSessionManager(RunningSession()));

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    result.Accepted.Should().Be(1);
    result.StepOutcomes.Should().ContainSingle().Which.Status.Should().Be("executed");
  }

  [Fact]
  public async Task EnsureGameRunningFailureOutcomeReturnsAccepted0AndGameNotRunningStatus() {
    var cmds = new FakeCommandRepository();
    cmds.Seed(EnsureRunningCommand());
    var executor = BuildExecutor(
      new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameNotRunning)),
      cmds, new FakeSessionManager(RunningSession()));

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().ContainSingle().Which.Status.Should().Be("game_not_running");
  }

  [Fact]
  public async Task EnsureGameRunningNullHandlerReturnsPlatformUnsupported() {
    var cmds = new FakeCommandRepository();
    cmds.Seed(EnsureRunningCommand());
    var executor = BuildExecutor(null, cmds, new FakeSessionManager(RunningSession()));

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().ContainSingle().Which.Status.Should().Be("platform_unsupported");
  }

  [Fact]
  public async Task EnsureGameRunningFailureCompletesWithoutThrowing() {
    // Verifies that a GameNotRunning failure outcome uses `continue` (not throw/return),
    // so ForceExecuteDetailedAsync returns normally rather than propagating an exception.
    var cmds = new FakeCommandRepository();
    cmds.Seed(EnsureRunningCommand());
    var executor = BuildExecutor(
      new StubHandler(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameNotRunning)),
      cmds, new FakeSessionManager(RunningSession()));

    var act = async () => await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    await act.Should().NotThrowAsync();
  }
}
