using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// The scheduling state of one queue run: the entry snapshot the run executes, its relative-timer
/// anchor, and — crucially — which work has already been consumed (time-of-day timers fired today,
/// relative timers fired once this run, once-per-run steps completed this cycle, and whether the
/// once-per-run pass is done at all).
///
/// The run loop owns and mutates it as it fires; the monitor projection only reads it. Before this
/// existed the two derived their plans independently — the loop from its own locals, the monitor from
/// the raw template — so the monitor happily listed steps the run had already finished and would never
/// repeat. Both now answer "what runs next" from the same state, and <see cref="ComputeNextDue"/> is
/// the single shared implementation of "when does this run wake up next" (used by the idle-pause
/// decision and by the monitor), so the two can no longer drift.
///
/// Written by the run-loop thread and read by monitor polls on another thread, so every member is
/// lock-guarded. Entries are keyed by their index in <see cref="Entries"/>.
/// </summary>
internal sealed class QueueRunSchedule {
  private readonly object _gate = new();
  private readonly Dictionary<int, DateOnly> _timeOfDayFiredOn = new();
  private readonly HashSet<int> _relativeFired = new();
  private readonly HashSet<int> _oncePerRunDoneThisCycle = new();
  private bool _oncePerRunPassDone;

  public QueueRunSchedule(IReadOnlyList<QueueTemplateEntry> entries, DateTimeOffset runStartedAt, bool cycling) {
    Entries = entries;
    RunStartedAt = runStartedAt;
    Cycling = cycling;
  }

  /// <summary>
  /// The entries this run actually executes, snapshotted at run start. The monitor reads these rather
  /// than re-fetching the template so a mid-run template edit cannot desynchronize the two (and so the
  /// consumed-work indices below always line up with what is displayed).
  /// </summary>
  public IReadOnlyList<QueueTemplateEntry> Entries { get; }

  /// <summary>The local-clock anchor relative-offset timers are measured from.</summary>
  public DateTimeOffset RunStartedAt { get; }

  /// <summary>Whether the run cycles (its once-per-run pass repeats indefinitely).</summary>
  public bool Cycling { get; }

  // ── Time-of-day timers (fire at most once per calendar day) ──────────────────────────────────

  /// <summary>Records that the time-of-day timer at <paramref name="index"/> fired on <paramref name="day"/>.</summary>
  public void MarkTimeOfDayFired(int index, DateOnly day) {
    lock (_gate) { _timeOfDayFiredOn[index] = day; }
  }

  /// <summary>True when that time-of-day timer has already fired on <paramref name="day"/>.</summary>
  public bool TimeOfDayFiredOn(int index, DateOnly day) {
    lock (_gate) { return _timeOfDayFiredOn.TryGetValue(index, out var last) && last == day; }
  }

  /// <summary>
  /// When the time-of-day timer at <paramref name="index"/> is next eligible to fire. Mirrors the run
  /// loop's own condition (<c>now &gt;= tod &amp;&amp; not fired today</c>): already fired today → tomorrow at
  /// the same wall-clock time; still ahead today → today at that time; past today but not yet fired →
  /// <paramref name="now"/>, because the loop fires it on its very next iteration (catch-up).
  /// </summary>
  public DateTimeOffset NextTimeOfDayDue(int index, TimeOnly tod, DateTimeOffset now) {
    var today = DateOnly.FromDateTime(now.DateTime);
    if (TimeOfDayFiredOn(index, today)) {
      return new DateTimeOffset(today.AddDays(1).ToDateTime(tod), now.Offset);
    }
    var todayAt = new DateTimeOffset(today.ToDateTime(tod), now.Offset);
    return now.TimeOfDay <= tod.ToTimeSpan() ? todayAt : now;
  }

  // ── Relative-offset timers (fire at most once per run) ───────────────────────────────────────

  /// <summary>Records that the relative-offset timer at <paramref name="index"/> has fired this run.</summary>
  public void MarkRelativeFired(int index) {
    lock (_gate) { _relativeFired.Add(index); }
  }

  /// <summary>True once that relative-offset timer has fired (it never fires again this run).</summary>
  public bool RelativeFired(int index) {
    lock (_gate) { return _relativeFired.Contains(index); }
  }

  /// <summary>True while any relative-offset timer of this run has yet to fire.</summary>
  public bool HasUnfiredRelativeTimers {
    get {
      lock (_gate) {
        for (var i = 0; i < Entries.Count; i++) {
          if (IsTimer(Entries[i]) && Entries[i].TimerRelativeOffset is not null && !_relativeFired.Contains(i)) {
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
    lock (_gate) { _oncePerRunDoneThisCycle.Add(index); }
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
  /// Whether an every-step firing can still happen. Every-step sequences piggyback on the once-per-run
  /// pass (and, with no once-per-run entries, run exactly once with it), so once a non-cycling run's
  /// pass is done none can follow — the run stays alive purely to service timers.
  /// </summary>
  public bool EveryStepCanStillRun => Cycling || !OncePerRunPassDone;

  /// <summary>
  /// The once-per-run entries this run has still to execute, in template order. Empty for a
  /// non-cycling run whose pass is done; a cycling run whose current cycle is exhausted reports the
  /// next cycle's full spine.
  /// </summary>
  public IReadOnlyList<QueueTemplateEntry> RemainingOncePerRun() {
    lock (_gate) {
      if (_oncePerRunPassDone && !Cycling) return Array.Empty<QueueTemplateEntry>();

      var remaining = new List<QueueTemplateEntry>();
      for (var i = 0; i < Entries.Count; i++) {
        if (Entries[i].ScheduleType != ScheduleType.OncePerRun) continue;
        if (_oncePerRunDoneThisCycle.Contains(i)) continue;
        remaining.Add(Entries[i]);
      }
      if (remaining.Count == 0 && Cycling) {
        remaining.AddRange(Entries.Where(e => e.ScheduleType == ScheduleType.OncePerRun));
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

    for (var i = 0; i < Entries.Count; i++) {
      var entry = Entries[i];
      if (!IsTimer(entry)) continue;
      if (entry.TimerTimeOfDay is { } tod) Consider(NextTimeOfDayDue(i, tod, now));
      if (entry.TimerRelativeOffset is { } offset && !RelativeFired(i)) Consider(RunStartedAt + offset);
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
