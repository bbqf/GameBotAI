using System.Collections.Concurrent;

namespace GameBot.Service.Security;

/// <summary>
/// An in-memory limit on failed attempts for each source (feature 187, research R8).
/// 5 failed attempts in 5 minutes block that source for 15 minutes. A different source is not blocked.
/// </summary>
internal sealed class FailedAttemptLimiter {
  internal const int MaxFailures = 5;
  internal static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
  internal static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(15);
  private const int MaxSources = 5000;

  private readonly TimeProvider _time;
  private readonly ConcurrentDictionary<string, SourceState> _sources = new(StringComparer.Ordinal);

  public FailedAttemptLimiter(TimeProvider time) {
    _time = time ?? throw new ArgumentNullException(nameof(time));
  }

  /// <summary>True when the source is blocked now.</summary>
  public bool IsBlocked(string source) {
    ArgumentNullException.ThrowIfNull(source);
    if (!_sources.TryGetValue(source, out var state)) {
      return false;
    }

    lock (state) {
      return state.BlockedUntil > _time.GetUtcNow();
    }
  }

  /// <summary>Counts one failed attempt. Returns true when this failure starts a block.</summary>
  public bool RegisterFailure(string source) {
    ArgumentNullException.ThrowIfNull(source);
    var now = _time.GetUtcNow();
    if (_sources.Count > MaxSources) {
      Prune(now);
    }

    var state = _sources.GetOrAdd(source, _ => new SourceState());
    lock (state) {
      state.Failures.RemoveAll(t => now - t > Window);
      state.Failures.Add(now);
      if (state.Failures.Count >= MaxFailures && state.BlockedUntil <= now) {
        state.BlockedUntil = now + BlockDuration;
        state.Failures.Clear();
        return true;
      }

      return false;
    }
  }

  private void Prune(DateTimeOffset now) {
    foreach (var pair in _sources) {
      lock (pair.Value) {
        pair.Value.Failures.RemoveAll(t => now - t > Window);
        if (pair.Value.Failures.Count == 0 && pair.Value.BlockedUntil <= now) {
          _sources.TryRemove(pair.Key, out _);
        }
      }
    }
  }

  private sealed class SourceState {
    public List<DateTimeOffset> Failures { get; } = new();

    public DateTimeOffset BlockedUntil { get; set; } = DateTimeOffset.MinValue;
  }
}
