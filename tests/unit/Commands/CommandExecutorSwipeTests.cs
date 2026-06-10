using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Sessions;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Commands;

public sealed class CommandExecutorSwipeTests {
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

  private sealed class RecordingSessionManager : ISessionManager {
    private readonly EmulatorSession _session;
    public List<InputAction> ReceivedInputs { get; } = new();
    public int SendResult { get; set; } = 1;
    /// <summary>Optional per-action mutation simulating the real SessionManager's in-place jitter.</summary>
    public Action<InputAction>? OnSend { get; set; }
    public RecordingSessionManager(EmulatorSession session) => _session = session;
    public int ActiveCount => 1;
    public bool CanCreateSession => false;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public EmulatorSession CreateSession(string g, string? s = null) => throw new NotSupportedException();
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) {
      foreach (var action in actions) {
        OnSend?.Invoke(action);
        ReceivedInputs.Add(action);
      }
      return Task.FromResult(SendResult);
    }
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

  // ── Helpers ───────────────────────────────────────────────────────────────

  private static EmulatorSession RunningSession(string id = "session1") =>
    new() { Id = id, GameId = "queue:q1", Status = SessionStatus.Running, DeviceSerial = "emulator-5554" };

  private static Command SwipeCommand(int startX, int startY, int endX, int endY, int? durationMs, string id = "cmd1") => new() {
    Id = id,
    Name = "Swipe",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() {
        Type = CommandStepType.Swipe,
        Swipe = new SwipeConfig { StartX = startX, StartY = startY, EndX = endX, EndY = endY, DurationMs = durationMs },
        Order = 1
      }
    }
  };

  private static CommandExecutor BuildExecutor(FakeCommandRepository cmds, RecordingSessionManager sessions) =>
    new(cmds, sessions, new FakeTriggerRepository(),
        new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
        NullLogger<CommandExecutor>.Instance,
        new FakeSessionContextCache());

  // ── Tests ─────────────────────────────────────────────────────────────────

  [Fact]
  public async Task SwipeStepDispatchesInputActionWithCorrectCoordinateArgs() {
    var cmds = new FakeCommandRepository();
    var sessions = new RecordingSessionManager(RunningSession());
    cmds.Seed(SwipeCommand(100, 200, 300, 400, null));
    var executor = BuildExecutor(cmds, sessions);

    await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    sessions.ReceivedInputs.Should().ContainSingle();
    var sent = sessions.ReceivedInputs[0];
    sent.Type.Should().Be("swipe");
    sent.Args.Should().ContainKey("x1").WhoseValue.Should().Be(100);
    sent.Args.Should().ContainKey("y1").WhoseValue.Should().Be(200);
    sent.Args.Should().ContainKey("x2").WhoseValue.Should().Be(300);
    sent.Args.Should().ContainKey("y2").WhoseValue.Should().Be(400);
  }

  [Fact]
  public async Task SwipeStepWithDurationIncludesDurationMsInArgs() {
    var cmds = new FakeCommandRepository();
    var sessions = new RecordingSessionManager(RunningSession());
    cmds.Seed(SwipeCommand(0, 0, 100, 100, 300));
    var executor = BuildExecutor(cmds, sessions);

    await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    sessions.ReceivedInputs[0].Args.Should().ContainKey("durationMs").WhoseValue.Should().Be(300);
  }

  [Fact]
  public async Task SwipeStepWithoutDurationOmitsDurationMsFromArgs() {
    var cmds = new FakeCommandRepository();
    var sessions = new RecordingSessionManager(RunningSession());
    cmds.Seed(SwipeCommand(0, 0, 100, 100, null));
    var executor = BuildExecutor(cmds, sessions);

    await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    sessions.ReceivedInputs[0].Args.Should().NotContainKey("durationMs");
  }

  [Fact]
  public async Task SwipeStepCountsAsOneAccepted() {
    var cmds = new FakeCommandRepository();
    var sessions = new RecordingSessionManager(RunningSession()) { SendResult = 1 };
    cmds.Seed(SwipeCommand(0, 0, 100, 200, null));
    var executor = BuildExecutor(cmds, sessions);

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    result.Accepted.Should().Be(1);
    result.StepOutcomes.Should().ContainSingle().Which.Status.Should().Be("executed");
  }

  [Fact]
  public async Task SwipeStepWithNullConfigRecordsSkippedInvalidConfig() {
    var cmds = new FakeCommandRepository();
    var sessions = new RecordingSessionManager(RunningSession());
    cmds.Seed(new Command {
      Id = "cmd1",
      Name = "BadSwipe",
      TriggerId = null,
      Steps = new Collection<CommandStep> {
        new() { Type = CommandStepType.Swipe, Swipe = null, Order = 1 }
      }
    });
    var executor = BuildExecutor(cmds, sessions);

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    result.Accepted.Should().Be(0);
    sessions.ReceivedInputs.Should().BeEmpty();
    result.StepOutcomes.Should().ContainSingle().Which.Status.Should().Be("skipped_invalid_config");
  }

  [Fact]
  public async Task SwipeStepReportsTargetSwipeAndExecutedSwipeFromMutatedArgs() {
    var cmds = new FakeCommandRepository();
    var sessions = new RecordingSessionManager(RunningSession()) {
      // Simulate the real SessionManager's tap-point jitter mutating the args in place.
      OnSend = a => {
        a.Args["x1"] = 102;
        a.Args["y1"] = 199;
        a.Args["x2"] = 304;
        a.Args["y2"] = 405;
      }
    };
    cmds.Seed(SwipeCommand(100, 200, 300, 400, null));
    var executor = BuildExecutor(cmds, sessions);

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.TargetSwipe.Should().Be(new PrimitiveSwipePoints(
      new PrimitiveTapResolvedPoint(100, 200), new PrimitiveTapResolvedPoint(300, 400)));
    outcome.ExecutedSwipe.Should().Be(new PrimitiveSwipePoints(
      new PrimitiveTapResolvedPoint(102, 199), new PrimitiveTapResolvedPoint(304, 405)));
  }

  [Fact]
  public async Task SwipeStepExecutedSwipeEqualsTargetSwipeWhenArgsAreNotMutated() {
    var cmds = new FakeCommandRepository();
    var sessions = new RecordingSessionManager(RunningSession());
    cmds.Seed(SwipeCommand(100, 200, 300, 400, null));
    var executor = BuildExecutor(cmds, sessions);

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.ExecutedSwipe.Should().Be(outcome.TargetSwipe);
  }

  [Fact]
  public async Task SwipeStepRecordsSwipeStepType() {
    var cmds = new FakeCommandRepository();
    var sessions = new RecordingSessionManager(RunningSession());
    cmds.Seed(SwipeCommand(10, 20, 30, 40, null));
    var executor = BuildExecutor(cmds, sessions);

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    result.StepOutcomes.Should().ContainSingle().Which.StepType.Should().Be("swipe");
  }
}
