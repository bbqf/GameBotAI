using System.Collections.Concurrent;
using GameBot.Domain.Quiz;

namespace GameBot.Service.Services.Quiz;

/// <summary>
/// Keeps the result of each <c>quiz-lookup</c> step for the run that made it (feature 142, research R7). A later
/// <c>quiz-learn</c> step of the same run reads it. The data is in memory only. A run opens its slot, and the slot is
/// removed when the run ends. An entry that is not removed (a run that never ended) expires after one hour.
/// </summary>
internal sealed class QuizLookupResultKeeper {
  /// <summary>How long an entry lives when its run never closes the slot.</summary>
  public static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(1);

  private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Entry>> _runs = new(StringComparer.Ordinal);
  private readonly TimeProvider _time;

  public QuizLookupResultKeeper(TimeProvider? timeProvider = null) {
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <summary>Opens the slot of a run. Dispose the returned object when the run ends.</summary>
  public IDisposable Open(string runKey) {
    ArgumentException.ThrowIfNullOrWhiteSpace(runKey);
    _runs.GetOrAdd(runKey, _ => new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal));
    return new Slot(this, runKey);
  }

  /// <summary>Keeps the result of a lookup step. It replaces an earlier result of the same step (a loop).</summary>
  public void Keep(string runKey, QuizLookupResult result) {
    ArgumentException.ThrowIfNullOrWhiteSpace(runKey);
    ArgumentNullException.ThrowIfNull(result);
    PurgeExpired();
    var slot = _runs.GetOrAdd(runKey, _ => new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal));
    slot[result.StepId] = new Entry(result, _time.GetUtcNow());
  }

  /// <summary>Removes the result of a lookup step, so that a lookup without a pick leaves no old result.</summary>
  public void Forget(string runKey, string stepId) {
    if (_runs.TryGetValue(runKey, out var slot)) {
      slot.TryRemove(stepId, out _);
    }
  }

  /// <summary>Gets the result of a lookup step, or null when the lookup did not run or kept none.</summary>
  public QuizLookupResult? Get(string runKey, string stepId) {
    if (_runs.TryGetValue(runKey, out var slot)
        && slot.TryGetValue(stepId, out var entry)
        && _time.GetUtcNow() - entry.KeptAt < EntryLifetime) {
      return entry.Result;
    }

    return null;
  }

  /// <summary>Removes the slot of a run.</summary>
  public void Close(string runKey) => _runs.TryRemove(runKey, out _);

  /// <summary>The number of open slots. For tests.</summary>
  internal int OpenSlotCount => _runs.Count;

  private void PurgeExpired() {
    var limit = _time.GetUtcNow() - EntryLifetime;
    foreach (var run in _runs) {
      foreach (var pair in run.Value) {
        if (pair.Value.KeptAt < limit) {
          run.Value.TryRemove(pair.Key, out _);
        }
      }

      if (run.Value.IsEmpty) {
        _runs.TryRemove(run.Key, out _);
      }
    }
  }

  private sealed record Entry(QuizLookupResult Result, DateTimeOffset KeptAt);

  private sealed class Slot : IDisposable {
    private readonly QuizLookupResultKeeper _owner;
    private readonly string _runKey;

    public Slot(QuizLookupResultKeeper owner, string runKey) {
      _owner = owner;
      _runKey = runKey;
    }

    public void Dispose() => _owner.Close(_runKey);
  }
}
