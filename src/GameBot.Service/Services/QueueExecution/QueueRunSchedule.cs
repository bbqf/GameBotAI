using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// The scheduling state of one queue run: the plan the run executes, its relative-timer anchor, and —
/// crucially — which work has already been consumed (time-of-day timers fired today, relative timers fired
/// once this run, once-per-run steps completed this cycle, and whether the once-per-run pass is done at all).
///
/// The run loop owns and mutates it as it fires; the monitor projection only reads it. Before this
/// existed the two derived their plans independently — the loop from its own locals, the monitor from
/// the raw template — so the monitor happily listed steps the run had already finished and would never
/// repeat. Both now answer "what runs next" from the same state, and <see cref="ComputeNextDue"/> is
/// the single shared implementation of "when does this run wake up next" (used by the idle-pause
/// decision and by the monitor), so the two can no longer drift.
///
/// Written by the run-loop thread and read by monitor polls on another thread, so every member is
/// lock-guarded. Since feature 171 the consumed state is keyed by the stable entry key of
/// <see cref="QueueRunPlan"/> (the entry ID, or <c>idx:&lt;position&gt;</c> for an entry of an old template), not by
/// the list index, so a live apply can add, remove, or move entries without a change of any timer. The
/// index-based members are kept: they map an index of the current plan to its key.
/// </summary>
internal sealed class QueueRunSchedule {
  private readonly object _gate = new();
  private readonly Dictionary<string, DateOnly> _timeOfDayFiredOn = new(StringComparer.Ordinal);
  private readonly Dictionary<string, DailyRetry> _dailyRetries = new(StringComparer.Ordinal);
  private readonly HashSet<string> _relativeFired = new(StringComparer.Ordinal);
  private readonly HashSet<string> _oncePerRunDoneThisCycle = new(StringComparer.Ordinal);
  private bool _oncePerRunPassDone;
  private QueueRunPlan _plan;

  /// <summary>A pending re-fire of a time-of-day entry whose sequence failed: when, and which attempt.</summary>
  internal readonly record struct DailyRetry(DateTimeOffset DueAt, int Attempt);

  public QueueRunSchedule(QueueRunPlan plan, DateTimeOffset runStartedAt, bool cycling) {
    ArgumentNullException.ThrowIfNull(plan);
    _plan = plan;
    RunStartedAt = runStartedAt;
    Cycling = cycling;
  }

  /// <summary>Builds the schedule of a run from entries that are already the enabled ones (the key is the ID, or the list position).</summary>
  public QueueRunSchedule(IReadOnlyList<QueueTemplateEntry> entries, DateTimeOffset runStartedAt, bool cycling)
    : this(QueueRunPlan.Build(entries), runStartedAt, cycling) {
  }

  /// <summary>
  /// The plan this run executes. A live apply swaps it (<see cref="SwapPlan"/>); everything else reads it.
  /// The monitor reads this rather than re-fetching the template so a mid-run template edit cannot
  /// desynchronize the two.
  /// </summary>
  public QueueRunPlan Plan {
    get { lock (_gate) { return _plan; } }
  }

  /// <summary>The entries of the current plan.</summary>
  public IReadOnlyList<QueueTemplateEntry> Entries => Plan.Entries;

  /// <summary>The local-clock anchor relative-offset timers are measured from.</summary>
  public DateTimeOffset RunStartedAt { get; }

  /// <summary>Whether the run cycles (its once-per-run pass repeats indefinitely).</summary>
  public bool Cycling { get; }

  // ── Plan swap (feature 171) ──────────────────────────────────────────────────────────────────

  /// <summary>
  /// Replaces the plan in one step. The consumed state of an entry that stays is not touched, so no timer
  /// moves (FR-008, FR-009).
  /// </summary>
  /// <param name="newPlan">The new plan. Its version must be higher than the current one.</param>
  /// <param name="droppedKeys">Keys of removed entries: all their consumed state goes away.</param>
  /// <param name="clearedKeys">Keys of entries with a selected schedule change: their consumed state goes away.</param>
  /// <param name="renameMap">
  /// Old key to new key, for the upgrade of a running queue (<c>idx:&lt;n&gt;</c> to the entry ID): the consumed
  /// state moves to the new key.
  /// </param>
  public void SwapPlan(
      QueueRunPlan newPlan,
      IEnumerable<string>? droppedKeys = null,
      IEnumerable<string>? clearedKeys = null,
      IReadOnlyDictionary<string, string>? renameMap = null) {
    ArgumentNullException.ThrowIfNull(newPlan);
    lock (_gate) {
      if (renameMap is not null) {
        foreach (var pair in renameMap) RenameKeyLocked(pair.Key, pair.Value);
      }
      if (droppedKeys is not null) {
        foreach (var key in droppedKeys) RemoveKeyLocked(key);
      }
      if (clearedKeys is not null) {
        foreach (var key in clearedKeys) RemoveKeyLocked(key);
      }
      _plan = newPlan;
    }
  }

  private void RemoveKeyLocked(string key) {
    _timeOfDayFiredOn.Remove(key);
    _dailyRetries.Remove(key);
    _relativeFired.Remove(key);
    _oncePerRunDoneThisCycle.Remove(key);
  }

  private void RenameKeyLocked(string oldKey, string newKey) {
    if (string.Equals(oldKey, newKey, StringComparison.Ordinal)) return;
    if (_timeOfDayFiredOn.Remove(oldKey, out var day)) _timeOfDayFiredOn[newKey] = day;
    if (_dailyRetries.Remove(oldKey, out var retry)) _dailyRetries[newKey] = retry;
    if (_relativeFired.Remove(oldKey)) _relativeFired.Add(newKey);
    if (_oncePerRunDoneThisCycle.Remove(oldKey)) _oncePerRunDoneThisCycle.Add(newKey);
  }

  // A monitor poll can hold an index of the plan from before a swap. A position that is now out of range
  // maps to an index key that no entry has, so the read gives "nothing consumed" and never throws.
  private string KeyAt(int index) =>
    index >= 0 && index < _plan.Keys.Count
      ? _plan.Keys[index]
      : QueueRunPlan.IndexKeyPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

  // ── Time-of-day timers (fire at most once per calendar day) ──────────────────────────────────

  /// <summary>Records that the time-of-day timer at <paramref name="index"/> fired on <paramref name="day"/>.</summary>
  public void MarkTimeOfDayFired(int index, DateOnly day) {
    lock (_gate) { _timeOfDayFiredOn[KeyAt(index)] = day; }
  }

  /// <summary>True when that time-of-day timer has already fired on <paramref name="day"/>.</summary>
  public bool TimeOfDayFiredOn(int index, DateOnly day) {
    lock (_gate) { return TimeOfDayFiredOnLocked(KeyAt(index), day); }
  }

  private bool TimeOfDayFiredOnLocked(string key, DateOnly day) =>
    _timeOfDayFiredOn.TryGetValue(key, out var last) && last == day;

  /// <summary>
  /// When the time-of-day timer at <paramref name="index"/> is next eligible to fire. Mirrors the run
  /// loop's own condition (<c>now &gt;= tod &amp;&amp; not fired today</c>): already fired today → tomorrow at
  /// the same wall-clock time; still ahead today → today at that time; past today but not yet fired →
  /// <paramref name="now"/>, because the loop fires it on its very next iteration (catch-up).
  /// </summary>
  public DateTimeOffset NextTimeOfDayDue(int index, TimeOnly tod, DateTimeOffset now) {
    lock (_gate) { return NextTimeOfDayDueLocked(KeyAt(index), tod, now); }
  }

  private DateTimeOffset NextTimeOfDayDueLocked(string key, TimeOnly tod, DateTimeOffset now) {
    var today = DateOnly.FromDateTime(now.DateTime);
    if (TimeOfDayFiredOnLocked(key, today)) {
      return new DateTimeOffset(today.AddDays(1).ToDateTime(tod), now.Offset);
    }
    var todayAt = new DateTimeOffset(today.ToDateTime(tod), now.Offset);
    return now.TimeOfDay <= tod.ToTimeSpan() ? todayAt : now;
  }

  // ── Daily retries ────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Arms (or re-arms) a retry of the time-of-day entry at <paramref name="index"/>. A time-of-day
  /// entry is marked fired for the day even when its sequence fails - otherwise the loop would re-run
  /// it on its very next iteration, in a tight loop - so the retry is tracked separately here, with
  /// its own due time and attempt count.
  /// </summary>
  public void ArmDailyRetry(int index, DateTimeOffset dueAt, int attempt) {
    lock (_gate) { _dailyRetries[KeyAt(index)] = new DailyRetry(dueAt, attempt); }
  }

  /// <summary>Drops any pending retry of that entry (its firing finally succeeded, or attempts ran out).</summary>
  public void ClearDailyRetry(int index) {
    lock (_gate) { _dailyRetries.Remove(KeyAt(index)); }
  }

  /// <summary>
  /// True while any daily retry is armed for an entry of the plan. Like a pending relative/live schedule, an
  /// armed retry is work this run still owes: a non-cyclic run that broke out of its loop here would take the
  /// retry with it and the daily would be lost after all. A retry of a disabled entry does not count: that
  /// entry does not run.
  /// </summary>
  public bool HasPendingDailyRetries {
    get { lock (_gate) { return _dailyRetries.Keys.Any(_plan.ContainsKey); } }
  }

  /// <summary>
  /// The entry indices whose retry is due at <paramref name="now"/>, each with the attempt number that
  /// armed it. A snapshot: the caller fires them, then clears or re-arms each one. A retry of an entry that is
  /// not in the plan (disabled) is skipped and kept.
  /// </summary>
  public IReadOnlyList<(int Index, int Attempt)> DueDailyRetries(DateTimeOffset now) {
    lock (_gate) {
      var due = new List<(int, int)>();
      foreach (var pair in _dailyRetries) {
        if (pair.Value.DueAt > now) continue;
        var index = _plan.IndexOfKey(pair.Key);
        if (index >= 0) due.Add((index, pair.Value.Attempt));
      }
      return due;
    }
  }

  // ── Relative-offset timers (fire at most once per run) ───────────────────────────────────────

  /// <summary>Records that the relative-offset timer at <paramref name="index"/> has fired this run.</summary>
  public void MarkRelativeFired(int index) {
    lock (_gate) { _relativeFired.Add(KeyAt(index)); }
  }

  /// <summary>True once that relative-offset timer has fired (it never fires again this run).</summary>
  public bool RelativeFired(int index) {
    lock (_gate) { return _relativeFired.Contains(KeyAt(index)); }
  }

  /// <summary>True while any relative-offset timer of this run has yet to fire.</summary>
  public bool HasUnfiredRelativeTimers {
    get {
      lock (_gate) {
        var entries = _plan.Entries;
        for (var i = 0; i < entries.Count; i++) {
          if (IsTimer(entries[i]) && entries[i].TimerRelativeOffset is not null && !_relativeFired.Contains(_plan.Keys[i])) {
            return true;
          }
        }
        return false;
      }
    }
  }

  // ── Once-per-run pass ────────────────────────────────────────────────────────────────────────

  /// <summary>Clears the per-cycle completion set at the top of a once-per-run pass.</summary>
  public void BeginCycle() {
    lock (_gate) { _oncePerRunDoneThisCycle.Clear(); }
  }

  /// <summary>Records that the once-per-run entry at <paramref name="index"/> ran in this cycle.</summary>
  public void MarkOncePerRunCompleted(int index) {
    lock (_gate) { _oncePerRunDoneThisCycle.Add(KeyAt(index)); }
  }

  /// <summary>
  /// True when the once-per-run entry at <paramref name="index"/> ran in this cycle (feature 106). A held
  /// once-per-run pass continues later with the first entry that did not run.
  /// </summary>
  public bool OncePerRunCompletedThisCycle(int index) {
    lock (_gate) { return _oncePerRunDoneThisCycle.Contains(KeyAt(index)); }
  }

  /// <summary>Records that the once-per-run/every-step pass has run (it repeats only when cycling).</summary>
  public void MarkOncePerRunPassDone() {
    lock (_gate) { _oncePerRunPassDone = true; }
  }

  /// <summary>Whether the once-per-run/every-step pass has run at least once.</summary>
  public bool OncePerRunPassDone {
    get { lock (_gate) { return _oncePerRunPassDone; } }
  }

  /// <summary>
  /// The once-per-run entries this run has still to execute, in plan order. Empty for a
  /// non-cyclic run whose pass is done; a cycling run whose current cycle is exhausted reports the
  /// next cycle's full spine.
  /// </summary>
  public IReadOnlyList<QueueTemplateEntry> RemainingOncePerRun() {
    lock (_gate) {
      if (_oncePerRunPassDone && !Cycling) return Array.Empty<QueueTemplateEntry>();

      var entries = _plan.Entries;
      var remaining = new List<QueueTemplateEntry>();
      for (var i = 0; i < entries.Count; i++) {
        if (entries[i].ScheduleType != ScheduleType.OncePerRun) continue;
        if (_oncePerRunDoneThisCycle.Contains(_plan.Keys[i])) continue;
        remaining.Add(entries[i]);
      }
      if (remaining.Count == 0 && Cycling) {
        remaining.AddRange(entries.Where(e => e.ScheduleType == ScheduleType.OncePerRun));
      }
      return remaining;
    }
  }

  // ── Next-due projection (shared by the run loop's idle-pause and the monitor) ─────────────────

  /// <summary>
  /// Earliest upcoming firing across every pending schedule source of this run: template timers
  /// (time-of-day next-eligible, relative anchor+offset, both skipping already-consumed ones), live
  /// schedules, self-reschedule Timer firings, and any queued self-reschedule work (effectively due
  /// now). Pure read; never mutates. Null when nothing is pending.
  /// </summary>
  public DateTimeOffset? ComputeNextDue(QueueRunHandle handle, DateTimeOffset now) {
    DateTimeOffset? earliest = null;
    void Consider(DateTimeOffset candidate) {
      if (earliest is null || candidate < earliest) earliest = candidate;
    }

    lock (_gate) {
      var entries = _plan.Entries;
      for (var i = 0; i < entries.Count; i++) {
        var entry = entries[i];
        if (!IsTimer(entry)) continue;
        var key = _plan.Keys[i];
        if (entry.TimerTimeOfDay is { } tod) Consider(NextTimeOfDayDueLocked(key, tod, now));
        if (entry.TimerRelativeOffset is { } offset && !_relativeFired.Contains(key)) Consider(RunStartedAt + offset);
      }

      // An armed daily retry is real pending work: without it here the idle pause would park the
      // emulator until the next scheduled entry and sleep straight through the retry.
      foreach (var pair in _dailyRetries) {
        if (_plan.ContainsKey(pair.Key)) Consider(pair.Value.DueAt);
      }
    }

    foreach (var firing in handle.SnapshotPendingTimerFirings()) {
      if (firing.FireAt is { } fireAt) Consider(fireAt);
    }
    foreach (var liveAt in handle.PendingLiveSchedules.Values) Consider(liveAt);
    // Any queued self-reschedule work that would drain this cycle is effectively due now.
    if (!handle.PendingOncePerRun.IsEmpty || !handle.PendingNextCycleStart.IsEmpty) Consider(now);

    return earliest;
  }

  private static bool IsTimer(QueueTemplateEntry entry) => entry.ScheduleType == ScheduleType.Timer;
}
