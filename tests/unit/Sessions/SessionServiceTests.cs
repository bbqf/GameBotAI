using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests.Sessions;

public sealed class SessionServiceTests {
  [Fact]
  public void StartSessionReplacesExistingAndUpdatesCache() {
    var manager = new FakeSessionManager();
    manager.Seed(new EmulatorSession {
      Id = "sess-old",
      GameId = "game-1",
      DeviceSerial = "emu-1",
      Status = SessionStatus.Running,
      StartTime = DateTimeOffset.UtcNow.AddMinutes(-10),
      LastActivity = DateTimeOffset.UtcNow.AddMinutes(-1)
    });
    manager.NextCreateId = "sess-new";

    var cache = new RecordingSessionContextCache();
    var service = new SessionService(manager, cache);

    var running = service.StartSession("game-1", "emu-1");

    running.SessionId.Should().Be("sess-new");
    manager.LastStopSessionId.Should().Be("sess-old");

    var snapshot = service.GetRunningSessions();
    snapshot.Should().ContainSingle();
    snapshot.Single().SessionId.Should().Be("sess-new");

    cache.SetCalls.Should().Contain(call => call.SessionId == "sess-old");
    cache.SetCalls.Should().Contain(call => call.SessionId == "sess-new");
    cache.ClearCalls.Should().Contain(call => call.GameId == "game-1" && call.EmulatorId == "emu-1");
  }

  [Fact]
  public void GetRunningSessionsRemovesStoppedAndClearsCache() {
    var manager = new FakeSessionManager();
    var seeded = manager.Seed(new EmulatorSession {
      Id = "sess-stale",
      GameId = "game-2",
      DeviceSerial = "emu-2",
      Status = SessionStatus.Running,
      StartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
      LastActivity = DateTimeOffset.UtcNow.AddMinutes(-2)
    });

    var cache = new RecordingSessionContextCache();
    var service = new SessionService(manager, cache);

    var initial = service.GetRunningSessions();
    initial.Should().ContainSingle(s => s.SessionId == "sess-stale");
    cache.SetCalls.Should().Contain(call => call.SessionId == "sess-stale");

    seeded.Status = SessionStatus.Stopped;

    var after = service.GetRunningSessions();
    after.Should().BeEmpty();
    cache.ClearCalls.Should().Contain(call => call.GameId == "game-2" && call.EmulatorId == "emu-2");
  }
}

file sealed class FakeSessionManager : ISessionManager {
  private readonly Dictionary<string, EmulatorSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

  public string? NextCreateId { get; set; }
  public string? LastStopSessionId { get; private set; }
  public bool CanCreateSessionFlag { get; set; } = true;

  public int ActiveCount => _sessions.Count;
  public bool CanCreateSession => CanCreateSessionFlag;

  public EmulatorSession Seed(EmulatorSession session) {
    _sessions[session.Id] = session;
    return session;
  }

  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) {
    var id = NextCreateId ?? Guid.NewGuid().ToString("N");
    var session = new EmulatorSession {
      Id = id,
      GameId = gameIdOrPath,
      DeviceSerial = preferredDeviceSerial,
      Status = SessionStatus.Running,
      StartTime = DateTimeOffset.UtcNow,
      LastActivity = DateTimeOffset.UtcNow
    };
    _sessions[id] = session;
    return session;
  }

  public EmulatorSession? GetSession(string id) {
    _sessions.TryGetValue(id, out var session);
    return session;
  }

  public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions.Values.ToList();

  public bool StopSession(string id) {
    LastStopSessionId = id;
    return _sessions.Remove(id);
  }

  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);

  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

file sealed class RecordingSessionContextCache : ISessionContextCache {
  public List<(string GameId, string EmulatorId, string SessionId)> SetCalls { get; } = new();
  public List<(string GameId, string EmulatorId)> ClearCalls { get; } = new();

  public void SetSessionId(string gameId, string adbSerial, string sessionId) {
    SetCalls.Add((gameId, adbSerial, sessionId));
  }

  public string? GetSessionId(string gameId, string adbSerial) => SetCalls.LastOrDefault(c => c.GameId == gameId && c.EmulatorId == adbSerial).SessionId;

  public void ClearSession(string gameId, string adbSerial) {
    ClearCalls.Add((gameId, adbSerial));
  }
}
