using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>The state of one template entry in a running queue (feature 184).</summary>
internal enum QueueEntryState {
  /// <summary>The sequence of the entry runs now.</summary>
  Running,

  /// <summary>A run-next request for the entry waits.</summary>
  Queued,

  /// <summary>The run still has a normal firing for the entry.</summary>
  Scheduled,

  /// <summary>The entry is enabled and has no pending firing. Run next is available.</summary>
  Idle
}

/// <summary>
/// Gives the state of the enabled entries of a run (feature 184). The run-next call and the monitor both use this
/// class, so the two cannot disagree. The rules are in <c>specs/184-showdown-run-next/data-model.md</c>.
/// </summary>
internal static class QueueEntryStateReader {
  /// <summary>The state text that the API and the UI use.</summary>
  public static string Text(QueueEntryState state) => state switch {
    QueueEntryState.Running => "running",
    QueueEntryState.Queued => "queued",
    QueueEntryState.Scheduled => "scheduled",
    _ => "idle"
  };

  /// <summary>
  /// Gives the state of the entry at <paramref name="index"/> of the current plan of the run.
  /// </summary>
  /// <param name="handle">The run handle (registers and the current sequence).</param>
  /// <param name="schedule">The schedule of the run (consumed work).</param>
  /// <param name="index">The index of an enabled entry in <see cref="QueueRunPlan.Entries"/>.</param>
  /// <param name="now">The local clock.</param>
  /// <param name="pendingBookingSequences">
  /// The sequence IDs that have a pending booking (self-reschedule timer, live schedule, or once-per-run and next-cycle
  /// booking). Build it one time with <see cref="PendingBookingSequences"/> for all entries.
  /// </param>
  public static QueueEntryState Read(
      QueueRunHandle handle,
      QueueRunSchedule schedule,
      int index,
      DateTimeOffset now,
      ISet<string> pendingBookingSequences) {
    var plan = schedule.Plan;
    var entry = plan.Entries[index];
    var key = plan.Keys[index];

    if (handle.IsRunNextQueued(key)) return QueueEntryState.Queued;
    if (string.Equals(handle.CurrentSequenceId, entry.SequenceId, StringComparison.Ordinal)) return QueueEntryState.Running;
    if (pendingBookingSequences.Contains(entry.SequenceId)) return QueueEntryState.Scheduled;

    return HasNormalFiring(schedule, entry, index, now) ? QueueEntryState.Scheduled : QueueEntryState.Idle;
  }

  /// <summary>The sequence IDs that have a pending booking on the run handle.</summary>
  public static HashSet<string> PendingBookingSequences(QueueRunHandle handle) {
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var firing in handle.SnapshotPendingTimerFirings()) set.Add(firing.SequenceId);
    foreach (var key in handle.PendingLiveSchedules.Keys) set.Add(key);
    foreach (var booking in handle.PendingOncePerRun) set.Add(booking.SequenceId);
    foreach (var booking in handle.PendingNextCycleStart) set.Add(booking.SequenceId);
    return set;
  }

  // True when the run still has a normal firing for the entry (the "scheduled" column of the data-model table).
  private static bool HasNormalFiring(QueueRunSchedule schedule, QueueTemplateEntry entry, int index, DateTimeOffset now) {
    switch (entry.ScheduleType) {
      case ScheduleType.OncePerRun:
        // A non-cycling run does not run the pass again after it is done. A cycling run resets the set at the next cycle.
        if (schedule.OncePerRunPassDone && !schedule.Cycling) return false;
        return !schedule.OncePerRunCompletedThisCycle(index);
      case ScheduleType.Timer:
        if (entry.TimerRelativeOffset is not null) return !schedule.RelativeFired(index);
        if (schedule.HasDailyRetry(index)) return true;
        return !schedule.TimeOfDayFiredOn(index, DateOnly.FromDateTime(now.DateTime));
      default:
        // At-queue-start, before-each-run, and every-step entries.
        return !schedule.EntryRan(index);
    }
  }

  /// <summary>Gives the state text of each enabled entry that has an ID. An entry with an index key has no state.</summary>
  public static Dictionary<string, string> ReadAll(QueueRunHandle handle, QueueRunSchedule schedule, DateTimeOffset now) {
    var plan = schedule.Plan;
    var bookings = PendingBookingSequences(handle);
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < plan.Entries.Count; i++) {
      var key = plan.Keys[i];
      if (key.StartsWith(QueueRunPlan.IndexKeyPrefix, StringComparison.Ordinal)) continue;
      result[key] = Text(Read(handle, schedule, i, now, bookings));
    }
    return result;
  }
}
