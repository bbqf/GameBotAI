using GameBot.Service.Endpoints.Dto;

namespace GameBot.Service.Services.Monitoring;

/// <summary>Feature 141: the last restart of one session. The record is not changed after it is made.</summary>
internal sealed record SessionRestartRecord(
  string SessionId,
  string State,
  string Step,
  DateTimeOffset StartedAtUtc,
  DateTimeOffset? EndedAtUtc,
  string? Reason) {
  /// <summary>True while the restart runs.</summary>
  public bool IsRunning => State == RestartStates.Restarting;

  /// <summary>Maps the record to its API shape.</summary>
  public SessionRestartRecordDto ToDto() => new() {
    SessionId = SessionId,
    State = State,
    Step = Step,
    StartedAtUtc = StartedAtUtc,
    EndedAtUtc = EndedAtUtc,
    Reason = Reason
  };
}

/// <summary>
/// Feature 141 (FR-013): the in-memory store of the last restart record of each session. The check and the
/// set of a new record are one atomic step, so two requests cannot both start a restart.
/// </summary>
internal sealed class SessionRestartStore {
  private readonly object _gate = new();
  private readonly Dictionary<string, SessionRestartRecord> _records = new(StringComparer.Ordinal);

  /// <summary>
  /// Makes a new record in state <c>restarting</c> when no restart runs for the session. Returns false and
  /// the running record when a restart runs. The running record does not change.
  /// </summary>
  public bool TryBegin(string sessionId, DateTimeOffset now, out SessionRestartRecord record) {
    lock (_gate) {
      if (_records.TryGetValue(sessionId, out var existing) && existing.IsRunning) {
        record = existing;
        return false;
      }

      record = new SessionRestartRecord(sessionId, RestartStates.Restarting, RestartSteps.Starting, now, null, null);
      _records[sessionId] = record;
      return true;
    }
  }

  /// <summary>Returns the last record of the session, or null.</summary>
  public SessionRestartRecord? Get(string sessionId) {
    lock (_gate) return _records.TryGetValue(sessionId, out var record) ? record : null;
  }

  /// <summary>Sets the step of a running record. A record that is not running does not change.</summary>
  public void SetStep(string sessionId, string step) {
    lock (_gate) {
      if (_records.TryGetValue(sessionId, out var record) && record.IsRunning) {
        _records[sessionId] = record with { Step = step };
      }
    }
  }

  /// <summary>Ends a running record with the state <c>success</c> or <c>failure</c>.</summary>
  public void End(string sessionId, string state, string step, string? reason, DateTimeOffset now) {
    lock (_gate) {
      if (_records.TryGetValue(sessionId, out var record) && record.IsRunning) {
        _records[sessionId] = record with { State = state, Step = step, Reason = reason, EndedAtUtc = now };
      }
    }
  }

  /// <summary>True while a restart runs for the session.</summary>
  public bool IsRestarting(string sessionId) {
    lock (_gate) return _records.TryGetValue(sessionId, out var record) && record.IsRunning;
  }

  /// <summary>Drops the records of sessions that no longer exist, except records that still run.</summary>
  public void Prune(IReadOnlyCollection<string> liveSessionIds) {
    ArgumentNullException.ThrowIfNull(liveSessionIds);
    lock (_gate) {
      var drop = _records.Where(p => !p.Value.IsRunning && !liveSessionIds.Contains(p.Key)).Select(p => p.Key).ToList();
      foreach (var key in drop) _records.Remove(key);
    }
  }
}
