namespace GameBot.Domain.WaitForStill;

/// <summary>
/// Decides when the screen is still (feature 174). The class has no clock and no capture. The caller gives
/// the time of each frame. The first good frame is the baseline: call <see cref="Baseline"/>. For each next
/// frame call <see cref="Observe"/> with the difference to the previous frame. After a failed capture call
/// <see cref="Reset"/>, so that the next good frame is a new baseline.
/// </summary>
public sealed class WaitForStillTracker {
  private readonly TimeSpan _stable;
  private readonly double _maxDiff;
  private DateTimeOffset? _previousTime;
  private DateTimeOffset? _stableStart;

  public WaitForStillTracker(int stableMs, double maxDiff) {
    _stable = TimeSpan.FromMilliseconds(stableMs);
    _maxDiff = maxDiff;
  }

  /// <summary>True when a baseline frame is set.</summary>
  public bool HasBaseline => _previousTime is not null;

  /// <summary>Sets the baseline frame time. The stable time starts from zero.</summary>
  public void Baseline(DateTimeOffset now) {
    _previousTime = now;
    _stableStart = null;
  }

  /// <summary>
  /// Records the difference between the frame at <paramref name="now"/> and the previous frame. Returns true
  /// when the screen was the same for the stable time. Without a baseline the frame becomes the baseline
  /// and the result is false.
  /// </summary>
  public bool Observe(double difference, DateTimeOffset now) {
    if (_previousTime is null) {
      Baseline(now);
      return false;
    }

    if (difference <= _maxDiff) {
      _stableStart ??= _previousTime;
    }
    else {
      _stableStart = null;
    }

    _previousTime = now;
    return _stableStart is not null && now - _stableStart.Value >= _stable;
  }

  /// <summary>Clears the previous frame time and the stable start, after a failed capture.</summary>
  public void Reset() {
    _previousTime = null;
    _stableStart = null;
  }
}
