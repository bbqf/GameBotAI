using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ActionModel = GameBot.Domain.Actions.Action;
using DomainInputAction = GameBot.Domain.Actions.InputAction;
using SessionInputAction = GameBot.Emulator.Session.InputAction;
using EmulatorSession = GameBot.Domain.Sessions.EmulatorSession;

namespace GameBot.UnitTests;

public sealed class CommandExecutorSessionCacheTests {
  [Fact]
  public async Task ForceExecuteUsesCachedSessionWhenSessionIdMissing() {
    var actionRepo = new FakeActionRepository();
    actionRepo.Seed(new ActionModel {
      Id = "a-connect",
      Name = "Connect",
      GameId = "g1",
      Steps = new Collection<DomainInputAction> {
        new DomainInputAction { Type = "connect-to-game", Args = new Dictionary<string, object> { { "adbSerial", "device-1" }, { "gameId", "g1" } } }
      }
    });

    var commandRepo = new FakeCommandRepository();
    commandRepo.Seed(new Command {
      Id = "cmd1",
      Name = "Cmd",
      Steps = new Collection<CommandStep> { new() { Type = CommandStepType.Action, TargetId = "a-connect", Order = 0 } }
    });

    var sessionManager = new FakeSessionManager();
    sessionManager.Seed(new EmulatorSession {
      Id = "sid-123",
      GameId = "g1",
      Status = GameBot.Domain.Sessions.SessionStatus.Running,
      DeviceSerial = "device-1"
    });

    var cache = new SessionContextCache();
    cache.SetSessionId("g1", "device-1", "sid-123");

    var exec = new CommandExecutor(commandRepo, actionRepo, sessionManager, new FakeTriggerRepository(), new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()), NullLogger<CommandExecutor>.Instance, cache);

    var accepted = await exec.ForceExecuteAsync(null, "cmd1").ConfigureAwait(false);

    accepted.Should().Be(1);
    sessionManager.LastSessionId.Should().Be("sid-123");
  }

  [Fact]
  public async Task ForceExecuteThrowsWhenNoCachedSession() {
    var actionRepo = new FakeActionRepository();
    actionRepo.Seed(new ActionModel {
      Id = "a-connect",
      Name = "Connect",
      GameId = "g1",
      Steps = new Collection<DomainInputAction> {
        new DomainInputAction { Type = "connect-to-game", Args = new Dictionary<string, object> { { "adbSerial", "device-1" }, { "gameId", "g1" } } }
      }
    });

    var commandRepo = new FakeCommandRepository();
    commandRepo.Seed(new Command {
      Id = "cmd1",
      Name = "Cmd",
      Steps = new Collection<CommandStep> { new() { Type = CommandStepType.Action, TargetId = "a-connect", Order = 0 } }
    });

    var sessionManager = new FakeSessionManager();
    var cache = new SessionContextCache();

    var exec = new CommandExecutor(commandRepo, actionRepo, sessionManager, new FakeTriggerRepository(), new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()), NullLogger<CommandExecutor>.Instance, cache);

    var act = async () => await exec.ForceExecuteAsync(null, "cmd1").ConfigureAwait(false);
    await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*cached_session_not_found*").ConfigureAwait(false);
  }
}

file sealed class FakeActionRepository : IActionRepository {
  private readonly Dictionary<string, ActionModel> _store = new(StringComparer.OrdinalIgnoreCase);

  public void Seed(ActionModel action) => _store[action.Id] = action;

  public Task<ActionModel> AddAsync(ActionModel action, CancellationToken ct = default) {
    _store[action.Id] = action;
    return Task.FromResult(action);
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_store.Remove(id));

  public Task<ActionModel?> GetAsync(string id, CancellationToken ct = default) {
    _store.TryGetValue(id, out var act);
    return Task.FromResult<ActionModel?>(act);
  }

  public Task<IReadOnlyList<ActionModel>> ListAsync(string? gameId = null, CancellationToken ct = default) {
    var list = _store.Values.Where(a => gameId is null || string.Equals(a.GameId, gameId, StringComparison.OrdinalIgnoreCase)).ToList();
    return Task.FromResult((IReadOnlyList<ActionModel>)list);
  }

  public Task<ActionModel?> UpdateAsync(ActionModel action, CancellationToken ct = default) {
    _store[action.Id] = action;
    return Task.FromResult<ActionModel?>(action);
  }
}

file sealed class FakeCommandRepository : ICommandRepository {
  private readonly Dictionary<string, Command> _store = new(StringComparer.OrdinalIgnoreCase);

  public void Seed(Command command) => _store[command.Id] = command;

  public Task<Command> AddAsync(Command command, CancellationToken ct = default) {
    _store[command.Id] = command;
    return Task.FromResult(command);
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_store.Remove(id));

  public Task<Command?> GetAsync(string id, CancellationToken ct = default) {
    _store.TryGetValue(id, out var cmd);
    return Task.FromResult(cmd);
  }

  public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Command>)_store.Values.ToList());

  public Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) {
    _store[command.Id] = command;
    return Task.FromResult<Command?>(command);
  }
}

file sealed class FakeTriggerRepository : ITriggerRepository {
  public Task<Trigger> AddAsync(Trigger trigger, CancellationToken ct = default) {
    LastAdded = trigger;
    return Task.FromResult(trigger);
  }
  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(true);
  public Trigger? LastAdded { get; private set; }
  public Trigger? LastUpserted { get; private set; }
  public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Trigger?>(null);
  public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Trigger>)Array.Empty<Trigger>());
  public Task UpsertAsync(Trigger trigger, CancellationToken ct = default) {
    LastUpserted = trigger;
    return Task.CompletedTask;
  }
}

file sealed class FakeSessionManager : ISessionManager {
  private readonly Dictionary<string, EmulatorSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
  public string? LastSessionId { get; private set; }

  public void Seed(EmulatorSession sess) => _sessions[sess.Id] = sess;

  public int ActiveCount => _sessions.Count;
  public bool CanCreateSession => true;
  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotImplementedException();
  public EmulatorSession? GetSession(string id) {
    _sessions.TryGetValue(id, out var sess);
    return sess;
  }
  public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions.Values;
  public bool StopSession(string id) => _sessions.Remove(id);
  public Task<int> SendInputsAsync(string id, IEnumerable<SessionInputAction> actions, CancellationToken ct = default) {
    LastSessionId = id;
    return Task.FromResult(actions?.Count() ?? 0);
  }
  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}
