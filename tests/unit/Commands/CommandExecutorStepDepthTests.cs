using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
using GameBot.Service.Services.SequenceExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 167: the command run marks each step outcome with its nesting depth, and only the steps of the
/// command that the sequence step runs (depth 0) are readable as step states (FR-009a).
/// </summary>
public sealed class CommandExecutorStepDepthTests {
  private sealed class FakeCommandRepository : ICommandRepository {
    private readonly Dictionary<string, Command> _cmds = new(System.StringComparer.Ordinal);
    public void Seed(Command c) => _cmds[c.Id] = c;
    public Task<Command> AddAsync(Command c, CancellationToken ct = default) { _cmds[c.Id] = c; return Task.FromResult(c); }
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_cmds.TryGetValue(id, out var c) ? c : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Command>)_cmds.Values.ToList());
    public Task<Command?> UpdateAsync(Command c, CancellationToken ct = default) { _cmds[c.Id] = c; return Task.FromResult<Command?>(c); }
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_cmds.Remove(id));
  }

  private sealed class RecordingSessionManager : ISessionManager {
    private readonly EmulatorSession _session;
    public RecordingSessionManager(EmulatorSession session) => _session = session;
    public int ActiveCount => 1;
    public bool CanCreateSession => false;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public EmulatorSession CreateSession(string g, string? s = null) => throw new System.NotSupportedException();
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(1);
    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(new SessionInputDispatchResult(true, System.Array.Empty<InputActionResult>()));
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(System.Array.Empty<byte>());
  }

  private sealed class FakeTriggerRepository : ITriggerRepository {
    public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Trigger?>(null);
    public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Trigger>)System.Array.Empty<Trigger>());
    public Task UpsertAsync(Trigger t, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
  }

  private sealed class FakeSessionContextCache : ISessionContextCache {
    public void SetSessionId(string g, string a, string s) { }
    public string? GetSessionId(string g, string a) => null;
    public void ClearSession(string g, string a) { }
  }

  private static CommandStep KeyStep(string key, int order) => new() {
    Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = key }, Order = order
  };

  private static CommandExecutor BuildExecutor(FakeCommandRepository cmds) =>
    new(cmds, new RecordingSessionManager(new EmulatorSession { Id = "session1", GameId = "queue:q1", Status = SessionStatus.Running, DeviceSerial = "emulator-5554" }),
        new FakeTriggerRepository(),
        new TriggerEvaluationService(System.Array.Empty<ITriggerEvaluator>()),
        NullLogger<CommandExecutor>.Instance,
        new FakeSessionContextCache());

  private static FakeCommandRepository NestedCommands() {
    var cmds = new FakeCommandRepository();
    cmds.Seed(new Command {
      Id = "inner", Name = "Inner", TriggerId = null,
      Steps = new Collection<CommandStep> { KeyStep("A", 2), KeyStep("B", 3) }
    });
    cmds.Seed(new Command {
      Id = "outer", Name = "Outer", TriggerId = null,
      Steps = new Collection<CommandStep> {
        KeyStep("C", 1),
        new() { Type = CommandStepType.Command, TargetId = "inner", Order = 2 },
        KeyStep("D", 3)
      }
    });
    return cmds;
  }

  [Fact]
  public async Task TopLevelStepsHaveDepthZeroAndNestedStepsHaveDepthOneOrMore() {
    var executor = BuildExecutor(NestedCommands());

    var result = await executor.ForceExecuteDetailedAsync("session1", "outer");

    result.StepOutcomes.Should().HaveCount(4);
    result.StepOutcomes.Where(o => o.Depth == 0).Select(o => o.StepOrder).Should().Equal(1, 3);
    result.StepOutcomes.Where(o => o.Depth == 1).Select(o => o.StepOrder).Should().Equal(2, 3);
    result.StepOutcomes.Should().OnlyContain(o => o.Status == "executed");
  }

  [Fact]
  public async Task ANestedStepWithTheSameOrderDoesNotChangeTheStepStates() {
    var executor = BuildExecutor(NestedCommands());
    var result = await executor.ForceExecuteDetailedAsync("session1", "outer");

    var outcome = SequenceExecutionService.ClassifyDispatch(result, CancellationToken.None);

    outcome.StepStates.Should().NotBeNull();
    outcome.StepStates!.Keys.Should().BeEquivalentTo(new[] { 1, 3 });
    outcome.StepStates[3].Should().Be("executed");
  }

  [Fact]
  public void OnlyReadableStatusesAreKeptAndTheNestedStepIsLeftOut() {
    var outcomes = new List<PrimitiveTapStepOutcome> {
      new(1, "skipped_detection_failed", "detection_failed_after_3_retries", null, null),
      new(2, "failed", "x", null, null),
      new(3, "image_detected", null, null, null),
      new(3, "completed_timeout", null, null, null, Depth: 1),
      new(4, "completed_timeout", null, null, null)
    };

    var states = SequenceExecutionService.ClassifyDispatch(new CommandForceExecutionResult(0, outcomes), CancellationToken.None).StepStates;

    states.Should().BeEquivalentTo(new Dictionary<int, string> {
      [1] = "skipped_detection_failed", [3] = "image_detected", [4] = "completed_timeout"
    });
  }

  [Fact]
  public void NoReadableStateGivesNull() {
    var outcomes = new List<PrimitiveTapStepOutcome> { new(1, "failed", "x", null, null) };

    SequenceExecutionService.ClassifyDispatch(new CommandForceExecutionResult(0, outcomes), CancellationToken.None)
      .StepStates.Should().BeNull();
  }
}
