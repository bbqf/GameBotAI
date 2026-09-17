using System;
using System.Collections.Generic;
using System.Linq;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// One completed cycle of a queue run — a single pass of the run loop over the roster's once-per-run
/// entries (feature 086). Only completed cycles become records; a cycle interrupted by a stop, a lost
/// connection or host shutdown is discarded with the handle and never published.
/// </summary>
/// <param name="Ordinal">1-based position within the run. Keeps counting past the retention bound.</param>
/// <param name="StartedAt">Local-clock instant the cycle opened.</param>
/// <param name="CompletedAt">Local-clock instant the cycle completed.</param>
/// <param name="Succeeded">False iff at least one entry in the cycle failed; an entry-less cycle succeeds.</param>
/// <param name="Entries">Per-entry outcomes in execution order.</param>
internal sealed record QueueCycleRecord(
  int Ordinal,
  DateTimeOffset StartedAt,
  DateTimeOffset CompletedAt,
  bool Succeeded,
  IReadOnlyList<QueueCycleEntryOutcome> Entries);

/// <summary>
/// The outcome of one sequence firing inside a cycle. The sequence's display name is deliberately not
/// stored — it is resolved when a response is built, so the ledger can never hold a stale name and
/// never needs a repository.
/// </summary>
internal sealed record QueueCycleEntryOutcome(string SequenceId, bool Succeeded);

/// <summary>
/// Value snapshot of a run's cycle health, safe to hold after the ledger's lock is released.
/// The three <c>LastCycle*</c> values are null together, exactly until the first cycle completes.
/// </summary>
internal sealed record QueueCycleHealth(
  int CyclesCompleted,
  DateTimeOffset? LastCycleStartedAt,
  DateTimeOffset? LastCycleCompletedAt,
  bool? LastCycleSucceeded,
  int ConsecutiveFailedCycles,
  int? CurrentEntryIndex);

/// <summary>
/// A queue run's bounded, in-memory record of the cycles it has completed (feature 086, issue #180).
/// <para>
/// A cycling run is otherwise indistinguishable from a run doing nothing: <c>status: Running</c> says
/// only that the loop was started and has not exited. The engine already counts cycles internally to
/// report "across N cycles" in its terminating summary; this ledger publishes that same count, plus
/// per-cycle outcomes, while the run is still alive.
/// </para>
/// <para>
/// <b>The ledger is a pure observer.</b> Every mutator returns void and is never consulted by a
/// scheduling decision <i>from within this type</i> — removing it entirely would not change which
/// sequences run, in what order, or when. That property is what makes it safe to add to the engine
/// that drives production farms, and it must be preserved by anything added here.
/// </para>
/// <para>
/// <b>Qualified by feature 087 (issue #181), deliberately.</b> A queue's failure policy now <i>acts</i>
/// on what this ledger publishes: <see cref="QueueFailurePolicyEvaluator"/> reads
/// <see cref="SnapshotHealth"/> after each cycle and may notify, pause or stop the run. That evaluator
/// is a separate type by design — this one still has no policy knowledge, takes no dependency on the
/// notifier, and returns nothing from any mutator. Keeping the decision outside the lock-holding
/// observer is what makes acting on a failing run safe; do not move policy logic in here.
/// </para>
/// <para>
/// All state is guarded by one lock, and every read returns a copy taken under it, so a reader never
/// holds the lock while the run loop wants it. This mirrors
/// <see cref="QueueRunHandle.SnapshotPendingTimerFirings"/>.
/// </para>
/// </summary>
internal sealed class QueueCycleLedger {
  /// <summary>
  /// How many completed cycles a run retains; the oldest is discarded first. Bounds memory for a queue
  /// that cycles indefinitely — a week-long run costs the same as a one-hour run. Deeper history is the
  /// execution log's job, not a live monitor's.
  /// </summary>
  public const int MaxRetainedCycles = 50;

  /// <summary>Default number of cycles returned when a caller names no limit.</summary>
  public const int DefaultCycleLimit = 20;

  private readonly object _lock = new();
  private readonly Queue<QueueCycleRecord> _completed = new();
  private OpenCycle? _open;
  private int _cyclesCompleted;
  private int _consecutiveFailedCycles;
  private int? _currentEntryIndex;

  /// <summary>A cycle in flight. Never published; dropped if the run ends before it completes.</summary>
  private sealed class OpenCycle {
    public DateTimeOffset StartedAt { get; init; }
    public List<QueueCycleEntryOutcome> Entries { get; } = new();
  }

  /// <summary>
  /// Opens a cycle if none is open; otherwise does nothing. Called at the top of every run-loop
  /// iteration. Idempotence is what makes one rule serve both queue kinds: a cycling run opens and
  /// completes one cycle per iteration, while a non-cycling run's trailing timer-poll iterations reuse
  /// the cycle they never complete, so it is never published.
  /// </summary>
  public void EnsureOpen(DateTimeOffset now) {
    lock (_lock) {
      _open ??= new OpenCycle { StartedAt = now };
    }
  }

  /// <summary>
  /// Records one sequence firing's outcome against the open cycle. A no-op when no cycle is open, so
  /// callers never have to check first.
  /// </summary>
  public void RecordEntry(string sequenceId, bool succeeded) {
    if (string.IsNullOrEmpty(sequenceId)) return;
    lock (_lock) {
      _open?.Entries.Add(new QueueCycleEntryOutcome(sequenceId, succeeded));
    }
  }

  /// <summary>Marks which roster position the run is executing (set within the once-per-run pass only).</summary>
  public void SetCurrentEntryIndex(int index) {
    lock (_lock) { _currentEntryIndex = index; }
  }

  /// <summary>Clears the roster position, so it reads as absent between entries and outside the pass.</summary>
  public void ClearCurrentEntryIndex() {
    lock (_lock) { _currentEntryIndex = null; }
  }

  /// <summary>
  /// Seals the open cycle into a record: derives its outcome, assigns its ordinal, updates the
  /// completed and consecutive-failure counters, and trims the ring. A no-op when no cycle is open.
  /// Called at the run loop's existing cycle counter, so a cycle is published exactly when the engine
  /// counts one.
  /// </summary>
  public void CompleteOpen(DateTimeOffset now) {
    lock (_lock) {
      if (_open is null) return;
      var open = _open;
      _open = null;
      Seal(open.StartedAt, now, open.Entries.ToArray());
    }
  }

  /// <summary>
  /// Drops the open cycle if it has recorded no entries; otherwise does nothing. Called when a cycling
  /// run's loop iteration ran no sequence (feature 093, #200): that iteration is not a cycle, and
  /// without the discard the next cycle that does run work would inherit this one's start time and
  /// report the whole idle wait as cycle duration. Never touches counters or completed records.
  /// </summary>
  public void DiscardOpenIfEmpty() {
    lock (_lock) {
      if (_open is { Entries.Count: 0 }) _open = null;
    }
  }

  /// <summary>
  /// Records a single completed cycle that executed nothing — the empty-template case, where the run
  /// loop counts one cycle without iterating. Keeps an idle-but-alive queue distinguishable from a
  /// stalled one.
  /// </summary>
  public void RecordEmptyCycle(DateTimeOffset now) {
    lock (_lock) {
      var startedAt = _open?.StartedAt ?? now;
      _open = null;
      Seal(startedAt, now, Array.Empty<QueueCycleEntryOutcome>());
    }
  }

  /// <summary>Point-in-time health values for the run. Caller must not hold the lock.</summary>
  public QueueCycleHealth SnapshotHealth() {
    lock (_lock) {
      var last = _completed.Count > 0 ? _completed.Last() : null;
      return new QueueCycleHealth(
        _cyclesCompleted,
        last?.StartedAt,
        last?.CompletedAt,
        last?.Succeeded,
        _consecutiveFailedCycles,
        _currentEntryIndex);
    }
  }

  /// <summary>
  /// The most recent completed cycles, newest first, at most <paramref name="limit"/> of them.
  /// The limit is clamped to 1..<see cref="MaxRetainedCycles"/> rather than rejected.
  /// </summary>
  public IReadOnlyList<QueueCycleRecord> SnapshotCycles(int limit) {
    var take = ClampLimit(limit);
    lock (_lock) {
      return _completed.Reverse().Take(take).ToArray();
    }
  }

  /// <summary>Clamps a caller-supplied cycle limit into the supported range (never throws).</summary>
  public static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxRetainedCycles);

  /// <summary>
  /// Commits one completed cycle. Must be called with <see cref="_lock"/> held.
  /// </summary>
  private void Seal(
    DateTimeOffset startedAt,
    DateTimeOffset completedAt,
    QueueCycleEntryOutcome[] entries) {
    var succeeded = true;
    for (var i = 0; i < entries.Length; i++) {
      if (!entries[i].Succeeded) { succeeded = false; break; }
    }

    _cyclesCompleted++;
    _consecutiveFailedCycles = succeeded ? 0 : _consecutiveFailedCycles + 1;
    _completed.Enqueue(new QueueCycleRecord(
      _cyclesCompleted,
      startedAt,
      completedAt,
      succeeded,
      entries));
    while (_completed.Count > MaxRetainedCycles) _completed.Dequeue();
  }
}
