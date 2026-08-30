using System;
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
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 079 (FR-006/FR-007) for the command path: an explicit session must win however many are
/// running, and "several sessions, none named" must be reported as such instead of resolving to an
/// arbitrary device.
/// </summary>
public sealed class CommandExecutorSessionResolutionTests {
  private static Command TapCommand(string id = "cmd1") => new() {
    Id = id,
    Name = "Cmd",
    Steps = new Collection<CommandStep> {
      new() { Type = CommandStepType.PrimitiveTap, PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("template-1") }, Order = 0 }
    }
  };

  private static CommandExecutor NewExecutor(SessionResolutionFakeSessions sessions, SessionResolutionFakeCommands? commands = null) =>
    new(commands ?? new SessionResolutionFakeCommands(TapCommand()),
        sessions,
        new SessionResolutionFakeTriggers(),
        new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
        NullLogger<CommandExecutor>.Instance,
        new SessionContextCache());

  [Fact]
  public async Task AnExplicitSessionIsUsedEvenWithSeveralSessionsRunning() {
    var sessions = new SessionResolutionFakeSessions("s1", "s2", "s3");
    var exec = NewExecutor(sessions);

    // Before feature 079 this threw: the executor demanded exactly one running session even when the
    // caller had already named one, so a second queue broke the first queue's command steps.
    await exec.ForceExecuteAsync("s2", "cmd1").ConfigureAwait(false);

    sessions.LastGetSessionId.Should().Be("s2");
  }

  [Fact]
  public async Task WithNoSessionAndExactlyOneRunningThatOneIsUsed() {
    var sessions = new SessionResolutionFakeSessions("only");
    var exec = NewExecutor(sessions);

    await exec.ForceExecuteAsync(null, "cmd1").ConfigureAwait(false);

    sessions.LastGetSessionId.Should().Be("only");
  }

  [Fact]
  public async Task WithNoSessionAndNoneRunningTheSentinelIsThrown() {
    var exec = NewExecutor(new SessionResolutionFakeSessions());

    var act = async () => await exec.ForceExecuteAsync(null, "cmd1").ConfigureAwait(false);

    var thrown = await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*missing_session_context*").ConfigureAwait(false);
    thrown.And.InnerException.Should().BeNull("no device at all is not an ambiguity");
  }

  [Fact]
  public async Task WithNoSessionAndSeveralRunningTheAmbiguityIsNamed() {
    var exec = NewExecutor(new SessionResolutionFakeSessions("s1", "s2"));

    var act = async () => await exec.ForceExecuteAsync(null, "cmd1").ConfigureAwait(false);

    var thrown = await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*missing_session_context*").ConfigureAwait(false);
    thrown.And.InnerException!.Message.Should()
      .Be("2 device sessions are active; specify a sessionId for 'cmd1'");
  }

  [Fact]
  public async Task ForceExecuteStepUsesAnExplicitSessionWithSeveralRunning() {
    var sessions = new SessionResolutionFakeSessions("s1", "s2");
    var exec = NewExecutor(sessions);
    var step = new CommandStep { Type = CommandStepType.PrimitiveTap, PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("t") }, Order = 0 };

    await exec.ForceExecuteStepAsync("s2", step).ConfigureAwait(false);

    sessions.LastGetSessionId.Should().Be("s2");
  }

  [Fact]
  public async Task ForceExecuteStepReportsTheAmbiguityWhenNoSessionIsNamed() {
    var exec = NewExecutor(new SessionResolutionFakeSessions("s1", "s2", "s3"));
    var step = new CommandStep { Type = CommandStepType.PrimitiveTap, PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("t") }, Order = 0 };

    var act = async () => await exec.ForceExecuteStepAsync(null, step).ConfigureAwait(false);

    var thrown = await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*missing_session_context*").ConfigureAwait(false);
    thrown.And.InnerException!.Message.Should()
      .Be("3 device sessions are active; specify a sessionId for 'force-execute'");
  }
}

/// <summary>Session manager fake holding any number of running sessions.</summary>
internal sealed class SessionResolutionFakeSessions : ISessionManager {
  private readonly List<EmulatorSession> _sessions;

  public SessionResolutionFakeSessions(params string[] runningIds) {
    _sessions = runningIds
      .Select(id => new EmulatorSession { Id = id, GameId = "g", Status = SessionStatus.Running, DeviceSerial = $"dev-{id}" })
      .ToList();
  }

  /// <summary>The session id the executor actually resolved to and acted on.</summary>
  public string? LastGetSessionId { get; private set; }

  public int ActiveCount => _sessions.Count;
  public bool CanCreateSession => true;
  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();

  public EmulatorSession? GetSession(string id) {
    var found = _sessions.Find(s => s.Id == id);
    if (found is not null) LastGetSessionId = id;
    return found;
  }

  public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions;
  public bool StopSession(string id) => _sessions.RemoveAll(s => s.Id == id) > 0;
  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

internal sealed class SessionResolutionFakeCommands : ICommandRepository {
  private readonly Dictionary<string, Command> _store = new(StringComparer.OrdinalIgnoreCase);

  public SessionResolutionFakeCommands(params Command[] commands) {
    foreach (var c in commands) _store[c.Id] = c;
  }

  public Task<Command> AddAsync(Command command, CancellationToken ct = default) { _store[command.Id] = command; return Task.FromResult(command); }
  public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_store.TryGetValue(id, out var c) ? c : null);
  public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Command>)_store.Values.ToList());
  public Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) { _store[command.Id] = command; return Task.FromResult<Command?>(command); }
  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_store.Remove(id));
}

internal sealed class SessionResolutionFakeTriggers : ITriggerRepository {
  public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Trigger?>(null);
  public Task UpsertAsync(Trigger trigger, CancellationToken ct = default) => Task.CompletedTask;
  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
  public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Trigger>)Array.Empty<Trigger>());
}
