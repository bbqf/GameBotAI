using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 069: a GoToHomeScreen command step sends the Android HOME key (keycode 3) through the
/// session input pipeline. The game is left running — the step never force-stops it.
/// </summary>
public sealed class CommandExecutorGoToHomeScreenTests {
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
    public RecordingSessionManager(EmulatorSession session) => _session = session;
    public List<InputAction> Sent { get; } = new();
    public int ActiveCount => 1;
    public bool CanCreateSession => false;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public EmulatorSession CreateSession(string g, string? s = null) => throw new NotSupportedException();
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) {
      var list = actions.ToList();
      Sent.AddRange(list);
      return Task.FromResult(list.Count);
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

  private static EmulatorSession RunningSession(string id = "session1") =>
    new() { Id = id, GameId = "game-1", Status = SessionStatus.Running, DeviceSerial = "emulator-5554" };

  private static Command GoHomeCommand(string id = "cmd1") => new() {
    Id = id,
    Name = "GoHome",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() { Type = CommandStepType.GoToHomeScreen, TargetId = string.Empty, Order = 1 }
    }
  };

  private static CommandExecutor BuildExecutor(FakeCommandRepository cmds, ISessionManager sessions) =>
    new(cmds, sessions, new FakeTriggerRepository(),
        new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
        NullLogger<CommandExecutor>.Instance,
        new FakeSessionContextCache());

  [Fact]
  public async Task GoToHomeScreenStepSendsHomeKeyAndReportsExecuted() {
    var cmds = new FakeCommandRepository();
    cmds.Seed(GoHomeCommand());
    var sessions = new RecordingSessionManager(RunningSession());
    var executor = BuildExecutor(cmds, sessions);

    var result = await executor.ForceExecuteDetailedAsync("session1", "cmd1");

    result.Accepted.Should().Be(1);
    var outcome = result.StepOutcomes.Should().ContainSingle().Which;
    outcome.Status.Should().Be("executed");
    outcome.StepType.Should().Be("go-to-home-screen");

    var sent = sessions.Sent.Should().ContainSingle().Which;
    sent.Type.Should().Be("key");
    sent.Args.Should().ContainKey("keyCode");
    Convert.ToInt32(sent.Args["keyCode"], CultureInfo.InvariantCulture).Should().Be(3);
  }
}
