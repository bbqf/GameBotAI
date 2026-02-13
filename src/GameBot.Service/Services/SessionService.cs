// Suppress CA1515: service is consumed via public controllers
#pragma warning disable CA1515
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;

namespace GameBot.Service.Services;

public interface ISessionService
{
  IReadOnlyCollection<RunningSession> GetRunningSessions();
  RunningSession StartSession(string gameId, string emulatorId, CancellationToken ct = default);
  bool StopSession(string sessionId);
}

public sealed class SessionService : ISessionService
{
  private readonly ISessionManager _sessions;
  private readonly ISessionContextCache _cache;
  private readonly ConcurrentDictionary<string, RunningSession> _running = new(StringComparer.OrdinalIgnoreCase);
  private readonly object _gate = new();

  public SessionService(ISessionManager sessions, ISessionContextCache cache)
  {
    _sessions = sessions;
    _cache = cache;
  }

  public IReadOnlyCollection<RunningSession> GetRunningSessions()
  {
    lock (_gate)
    {
      SyncFromSessionManager();
      return _running.Values.ToList();
    }
  }

  public RunningSession StartSession(string gameId, string emulatorId, CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
    ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);

    var normalizedGameId = gameId.Trim();
    var normalizedEmulatorId = emulatorId.Trim();

    lock (_gate)
    {
      SyncFromSessionManager();
      var key = Key(normalizedGameId, normalizedEmulatorId);
      if (_running.TryGetValue(key, out var existing))
      {
        // Best-effort stop; remove from running list even if stop fails per spec
        _sessions.StopSession(existing.SessionId);
        _running.TryRemove(key, out _);
        _cache.ClearSession(existing.GameId, existing.EmulatorId);
      }

      if (!_sessions.CanCreateSession)
      {
        throw new InvalidOperationException("capacity_exceeded");
      }

      var session = _sessions.CreateSession(normalizedGameId, normalizedEmulatorId);
      var running = ToRunning(session, normalizedEmulatorId);
      _running[key] = running;
      _cache.SetSessionId(normalizedGameId, normalizedEmulatorId, session.Id);
      return running;
    }
  }

  public bool StopSession(string sessionId)
  {
    if (string.IsNullOrWhiteSpace(sessionId)) return false;

    lock (_gate)
    {
      SyncFromSessionManager();
      var pair = _running.FirstOrDefault(kvp => string.Equals(kvp.Value.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
      var stopped = _sessions.StopSession(sessionId);
      if (!string.IsNullOrWhiteSpace(pair.Key))
      {
        _running.TryRemove(pair.Key, out var removed);
        if (removed is not null)
        {
          _cache.ClearSession(removed.GameId, removed.EmulatorId);
        }
      }
      return stopped;
    }
  }

  private void SyncFromSessionManager()
  {
    var sessions = _sessions.ListSessions();
    var byId = sessions.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    // Remove missing sessions
    foreach (var kvp in _running.ToArray())
    {
      if (!byId.TryGetValue(kvp.Value.SessionId, out var sess))
      {
        _running.TryRemove(kvp.Key, out var removed);
        if (removed is not null)
        {
          _cache.ClearSession(removed.GameId, removed.EmulatorId);
        }
        continue;
      }

      // Update heartbeat/status snapshot
      if (sess.Status == SessionStatus.Stopped)
      {
        _running.TryRemove(kvp.Key, out var removed);
        if (removed is not null)
        {
          _cache.ClearSession(removed.GameId, removed.EmulatorId);
        }
        continue;
      }

      var updated = new RunningSession
      {
        SessionId = kvp.Value.SessionId,
        GameId = kvp.Value.GameId,
        EmulatorId = kvp.Value.EmulatorId,
        StartedAtUtc = kvp.Value.StartedAtUtc,
        LastHeartbeatUtc = sess.LastActivity.UtcDateTime,
        Status = sess.Status == SessionStatus.Running ? RunningSessionStatus.Running : RunningSessionStatus.Stopping
      };
      _running[kvp.Key] = updated;
    }

    // Add any sessions created outside this service so the running list stays accurate
    foreach (var sess in sessions)
    {
      if (sess.Status != SessionStatus.Running) continue;
      if (_running.Values.Any(r => string.Equals(r.SessionId, sess.Id, StringComparison.OrdinalIgnoreCase))) continue;
      var emulatorId = (sess.DeviceSerial ?? string.Empty).Trim();
      var gameId = sess.GameId.Trim();
      var key = Key(gameId, emulatorId);
      _running[key] = ToRunning(sess, emulatorId);
      if (!string.IsNullOrWhiteSpace(emulatorId))
      {
        _cache.SetSessionId(gameId, emulatorId, sess.Id);
      }
    }
  }

  private static string Key(string gameId, string emulatorId) => $"{gameId.Trim()}|{emulatorId.Trim()}";

  private static RunningSession ToRunning(EmulatorSession session, string emulatorId) => new()
  {
    SessionId = session.Id,
    GameId = session.GameId,
    EmulatorId = emulatorId,
    StartedAtUtc = session.StartTime.UtcDateTime,
    LastHeartbeatUtc = session.LastActivity.UtcDateTime,
    Status = RunningSessionStatus.Running
  };
}
#pragma warning restore CA1515
