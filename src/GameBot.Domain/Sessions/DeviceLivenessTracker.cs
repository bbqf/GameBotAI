namespace GameBot.Domain.Sessions;

/// <summary>
/// The default <see cref="IDeviceLivenessTracker"/> (feature 106). It keeps one record for each
/// session ID behind one lock. Only <see cref="LoopStarted"/> and <see cref="RecordInputStarted"/>
/// create a record, so a hung capture or input that ends after <see cref="Remove"/> cannot make the
/// record again.
/// </summary>
public sealed class DeviceLivenessTracker : IDeviceLivenessTracker {
  private readonly object _lock = new();
  private readonly Dictionary<string, Record> _records = new(StringComparer.Ordinal);
  private readonly TimeProvider _time;

  /// <summary>Creates a tracker. <paramref name="timeProvider"/> is the clock; the default is the system clock.</summary>
  public DeviceLivenessTracker(TimeProvider? timeProvider = null) {
    _time = timeProvider ?? TimeProvider.System;
  }

  private sealed class Record {
    public bool CaptureLoopRunning { get; set; }
    public DateTimeOffset? LoopStartedAt { get; set; }
    public DateTimeOffset? LastCaptureAt { get; set; }
    public DateTimeOffset? LastChangeAt { get; set; }
    public DateTimeOffset? FirstInputAfterChangeAt { get; set; }
    public DateTimeOffset? LastInputAt { get; set; }
    public InputOutcome? LastInputOutcome { get; set; }
  }

  /// <inheritdoc />
  public DateTimeOffset Now => _time.GetUtcNow();

  /// <inheritdoc />
  public void LoopStarted(string sessionId) {
    if (string.IsNullOrEmpty(sessionId)) return;
    var now = Now;
    lock (_lock) {
      // A restarted loop starts from nothing (edge case "Capture loop restarted").
      _records[sessionId] = new Record { CaptureLoopRunning = true, LoopStartedAt = now };
    }
  }

  /// <inheritdoc />
  public void LoopStopped(string sessionId) {
    if (string.IsNullOrEmpty(sessionId)) return;
    lock (_lock) {
      if (_records.TryGetValue(sessionId, out var record)) record.CaptureLoopRunning = false;
    }
  }

  /// <inheritdoc />
  public void RecordCapture(string sessionId, bool changed) {
    if (string.IsNullOrEmpty(sessionId)) return;
    var now = Now;
    lock (_lock) {
      if (!_records.TryGetValue(sessionId, out var record) || !record.CaptureLoopRunning) return;
      record.LastCaptureAt = now;
      if (changed) {
        record.LastChangeAt = now;
        record.FirstInputAfterChangeAt = null;
      }
    }
  }

  /// <inheritdoc />
  public void RecordInputStarted(string sessionId) {
    if (string.IsNullOrEmpty(sessionId)) return;
    var now = Now;
    lock (_lock) {
      if (!_records.TryGetValue(sessionId, out var record)) {
        record = new Record();
        _records[sessionId] = record;
      }
      record.LastInputAt = now;
      record.LastInputOutcome = InputOutcome.Pending;
      record.FirstInputAfterChangeAt ??= now;
    }
  }

  /// <inheritdoc />
  public void RecordInputCompleted(string sessionId, InputOutcome outcome) {
    if (string.IsNullOrEmpty(sessionId)) return;
    lock (_lock) {
      if (_records.TryGetValue(sessionId, out var record)) record.LastInputOutcome = outcome;
    }
  }

  /// <inheritdoc />
  public void Remove(string sessionId) {
    if (string.IsNullOrEmpty(sessionId)) return;
    lock (_lock) { _records.Remove(sessionId); }
  }

  /// <inheritdoc />
  public DeviceLivenessSample Sample(string sessionId, bool hasDevice, bool? transportReady = null) {
    lock (_lock) {
      if (string.IsNullOrEmpty(sessionId) || !_records.TryGetValue(sessionId, out var r)) {
        return new DeviceLivenessSample(hasDevice, transportReady, false, null, null, null, null, null, null);
      }
      return new DeviceLivenessSample(
        hasDevice,
        transportReady,
        r.CaptureLoopRunning,
        r.LoopStartedAt,
        r.LastCaptureAt,
        r.LastChangeAt,
        r.FirstInputAfterChangeAt,
        r.LastInputAt,
        r.LastInputOutcome);
    }
  }

  /// <inheritdoc />
  public bool HasCaptureData(string sessionId) {
    if (string.IsNullOrEmpty(sessionId)) return false;
    lock (_lock) {
      return _records.TryGetValue(sessionId, out var record) && record.LoopStartedAt is not null;
    }
  }
}
