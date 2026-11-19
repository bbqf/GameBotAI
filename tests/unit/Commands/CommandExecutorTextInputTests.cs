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

/// <summary>Feature 192: the executor runs a TextInput step and reports the typed length or a reason code.</summary>
public sealed class CommandExecutorTextInputTests {
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

    /// <summary>When set, the fake writes this value into Args["typedLength"], as the device path does.</summary>
    public int? DeviceTypedLength { get; set; }

    public RecordingSessionManager(EmulatorSession session) => _session = session;
    public int ActiveCount => 1;
    public bool CanCreateSession => false;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public EmulatorSession CreateSession(string g, string? s = null) => throw new NotSupportedException();
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) {
      foreach (var action in actions) {
        ReceivedInputs.Add(action);
        if (DeviceTypedLength is { } typed) action.Args["typedLength"] = typed;
      }
      return Task.FromResult(SendResult);
    }
    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));
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

  private static Command TextCommand(TextInputConfig? config) => new() {
    Id = "cmd1",
    Name = "TextInput",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() { Type = CommandStepType.TextInput, TextInput = config, Order = 1 }
    }
  };

  private static async Task<(CommandForceExecutionResult Result, RecordingSessionManager Sessions)> RunAsync(
      TextInputConfig? config, int sendResult = 1, int? deviceTypedLength = null) {
    var cmds = new FakeCommandRepository();
    cmds.Seed(TextCommand(config));
    var sessions = new RecordingSessionManager(RunningSession()) { SendResult = sendResult, DeviceTypedLength = deviceTypedLength };
    var executor = new CommandExecutor(cmds, sessions, new FakeTriggerRepository(),
        new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
        NullLogger<CommandExecutor>.Instance,
        new FakeSessionContextCache());
    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");
    return (result, sessions);
  }

  [Fact]
  public async Task ATextStepSendsOneTextActionAndReportsTheTypedLength() {
    var (result, sessions) = await RunAsync(new TextInputConfig { Text = "250" }, deviceTypedLength: 3);

    var action = sessions.ReceivedInputs.Should().ContainSingle().Subject;
    action.Type.Should().Be("text");
    action.Args["text"].Should().Be("250");
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("executed");
    outcome.StepType.Should().Be("text-input");
    outcome.Reason.Should().BeNull();
    outcome.TypedLength.Should().Be(3);
    result.Accepted.Should().Be(1);
  }

  [Fact]
  public async Task InStubModeTheTypedLengthIsTheTextLength() {
    var (result, _) = await RunAsync(new TextInputConfig { Text = "250 Fighters" });

    result.StepOutcomes.Should().ContainSingle().Which.TypedLength.Should().Be(12);
  }

  [Theory]
  [InlineData("", "text_input_empty_text")]
  [InlineData("é", "text_input_unsupported_character")]
  [InlineData("a\nb", "text_input_unsupported_character")]
  public async Task ATextThatFailsTheCheckIsNotAcceptedAndSendsNothing(string text, string reason) {
    var (result, sessions) = await RunAsync(new TextInputConfig { Text = text });

    sessions.ReceivedInputs.Should().BeEmpty();
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("not_accepted");
    outcome.Reason.Should().Be(reason);
    outcome.TypedLength.Should().Be(0);
    outcome.StepType.Should().Be("text-input");
  }

  [Fact]
  public async Task ATextThatIsTooLongIsNotAccepted() {
    var (result, sessions) = await RunAsync(new TextInputConfig { Text = new string('1', 501) });

    sessions.ReceivedInputs.Should().BeEmpty();
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("not_accepted");
    outcome.Reason.Should().Be("text_input_too_long");
    outcome.TypedLength.Should().Be(0);
  }

  [Fact]
  public async Task ADeviceThatDoesNotAcceptTheInputGivesTextInputNotAccepted() {
    var (result, _) = await RunAsync(new TextInputConfig { Text = "250 Fighters" }, sendResult: 0, deviceTypedLength: 4);

    result.Accepted.Should().Be(0);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("not_accepted");
    outcome.Reason.Should().Be("text_input_not_accepted");
    outcome.TypedLength.Should().Be(4);
  }

  [Fact]
  public async Task AStepWithNoConfigIsSkippedWithTheMissingConfigReason() {
    var (result, sessions) = await RunAsync(null);

    sessions.ReceivedInputs.Should().BeEmpty();
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("skipped_invalid_config");
    outcome.Reason.Should().Be("text_input_missing_config");
    outcome.TypedLength.Should().BeNull();
  }
}
