using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.ExecutionLog;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Pure projection of a queue's live run plan for the monitor panel. Reads the active
/// <see cref="QueueRunHandle"/> (via <see cref="IQueueRunRegistry"/>) and — for a running queue — the
/// <see cref="QueueRunSchedule"/> that run publishes, folding them with the current local wall-clock
/// into an ordered now/up-next snapshot. When the queue is not running it instead surfaces the
/// best-effort last finalized run outcome from the execution log. No side effects.
///
/// Projecting from the run's own schedule state (rather than re-deriving a plan from the linked
/// <see cref="QueueTemplate"/>) is deliberate: work the run has already consumed — finished
/// once-per-run steps, spent one-shot relative timers, time-of-day timers that fired today — must not
/// be listed as upcoming, and the projection must agree with the resume time an idle pause reports.
/// </summary>
internal sealed class QueueMonitorService : IQueueMonitorService {
  private readonly IQueueRunRegistry _registry;
  private readonly IQueueRepository _queues;
  private readonly IQueueTemplateRepository _templates;
  private readonly ISequenceRepository _sequences;
  private readonly IExecutionLogService _log;
  private readonly TimeProvider _timeProvider;

  public QueueMonitorService(
    IQueueRunRegistry registry,
    IQueueRepository queues,
    IQueueTemplateRepository templates,
    ISequenceRepository sequences,
    IExecutionLogService log,
    TimeProvider? timeProvider = null) {
    _registry = registry;
    _queues = queues;
    _templates = templates;
    _sequences = sequences;
    _log = log;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  public async Task<QueueMonitorSnapshot> BuildAsync(string queueId, CancellationToken ct = default) {
    var queue = await _queues.GetAsync(queueId).ConfigureAwait(false);
    var name = queue?.Name ?? string.Empty;

    // Not running → 200 envelope with best-effort last outcome (the endpoint maps a missing queue to 404).
    if (!_registry.TryGet(queueId, out var handle)) {
      var lastOutcome = await BuildLastOutcomeAsync(queueId, ct).ConfigureAwait(false);
      return new QueueMonitorSnapshot(
        queueId, name, Running: false, CycleExecution: queue?.CycleExecution ?? false,
        RunStartedAt: null, Current: null, Upcoming: Array.Empty<QueueMonitorItem>(),
        NothingScheduled: false, LastOutcome: lastOutcome);
    }

    var template = string.IsNullOrEmpty(queue?.LinkedTemplateId)
      ? null
      : await _templates.GetAsync(queue!.LinkedTemplateId).ConfigureAwait(false);
    var names = await ResolveSequenceNamesAsync().ConfigureAwait(false);
    var now = _timeProvider.GetLocalNow();
    var cycling = handle.CycleExecution;

    // The run publishes its own schedule state (entry snapshot + already-consumed work) on the handle;
    // projecting from that — rather than re-deriving an idealized plan from the template — is what
    // keeps "up next" honest about steps this run has already finished. The template fallback covers
    // only the brief window before the run loop assigns it (nothing consumed yet).
    var schedule = handle.Schedule
      ?? new QueueRunSchedule(template?.Entries.ToArray() ?? Array.Empty<QueueTemplateEntry>(), handle.RunStartedAt, cycling);

    var liveSchedules = handle.PendingLiveSchedules.ToArray();
    var selfTimerFirings = handle.SnapshotPendingTimerFirings();

    var current = BuildCurrent(handle, schedule, names, cycling);
    var upcoming = BuildUpcoming(schedule, names, now, cycling, current, liveSchedules, selfTimerFirings);

    // NothingScheduled (FR-011): running with nothing left to do of ANY kind. Derived from the
    // projection itself so the banner can never contradict the list next to it — a live-scheduled-only
    // run still has an upcoming item and so must NOT read as "nothing scheduled".
    var nothingScheduled = current is null && upcoming.Count == 0;

    return new QueueMonitorSnapshot(
      queueId, name, Running: true, CycleExecution: cycling, RunStartedAt: handle.RunStartedAt,
      Current: current, Upcoming: upcoming, NothingScheduled: nothingScheduled, LastOutcome: null);
  }

  // ── Current ("now") ────────────────────────────────────────────────────────────────────────

  private static QueueMonitorItem? BuildCurrent(
    QueueRunHandle handle, QueueRunSchedule schedule, IReadOnlyDictionary<string, string> names, bool cycling) {
    var sequenceId = handle.CurrentSequenceId;
    if (string.IsNullOrEmpty(sequenceId)) {
      // Idle-pause (feature 073): no sequence is executing, but the run is intentionally backed out
      // waiting for the next firing. Surface a synthetic "Idle Pause" current item with the resume
      // time so an idle queue never reads as hung. A real CurrentSequenceId always wins (above);
      // by invariant it is null while idle-paused.
      if (handle.IdlePausedUntil is { } resumeAt) {
        return new QueueMonitorItem(
          SequenceId: string.Empty,
          SequenceName: "Idle Pause",
          Stale: false,
          ScheduleKind: ScheduleKind.IdlePause,
          Reason: $"Game paused — resumes at {resumeAt:HH:mm}",
          ExpectedAt: resumeAt,
          RelativeLabel: "paused",
          Repeats: false,
          Order: 0);
      }
      return null;
    }

    // Best-effort schedule kind: the first template entry referencing this sequence, else OncePerRun.
    var entry = schedule.Entries.FirstOrDefault(e => string.Equals(e.SequenceId, sequenceId, StringComparison.Ordinal));
    var kind = entry is null ? ScheduleKind.OncePerRun : KindFor(entry);
    return NewItem(sequenceId, names, kind, ReasonFor(kind, entry), expectedAt: null,
      relativeLabel: "now", repeats: Repeats(kind, cycling), order: 0);
  }

  // ── Upcoming ("up next") ──────────────────────────────────────────────────────────────────

  private static List<QueueMonitorItem> BuildUpcoming(
    QueueRunSchedule schedule, IReadOnlyDictionary<string, string> names,
    DateTimeOffset now, bool cycling, QueueMonitorItem? current,
    KeyValuePair<string, DateTimeOffset>[] liveSchedules, IReadOnlyList<SelfRescheduleEntry> selfTimerFirings) {
    var entries = schedule.Entries;

    // (1) OncePerRun spine in template order — the "playlist" backbone, limited to the steps this run
    // has NOT already consumed. A non-cycling run whose once-per-run pass is done never runs them
    // again (it stays alive only to service timers), so its spine is empty; listing them there was
    // what made a purely timer-driven wait look like it was about to run a step. AtQueueStart is
    // omitted (already ran); the currently-executing step is excluded (Current holds it).
    var spine = schedule.RemainingOncePerRun().ToList();
    if (current is not null) {
      var idx = spine.FindIndex(e => string.Equals(e.SequenceId, current.SequenceId, StringComparison.Ordinal));
      if (idx >= 0) spine.RemoveAt(idx);
    }
    var spineItems = spine.Select((e, i) => NewItem(
      e.SequenceId, names, ScheduleKind.OncePerRun, ReasonFor(ScheduleKind.OncePerRun, e),
      expectedAt: null, relativeLabel: i == 0 ? "next" : "up next",
      repeats: Repeats(ScheduleKind.OncePerRun, cycling), order: 0)).ToList();

    // (2) EveryStep entries, surfaced once each as "After Every Step" (not interleaved per step), and
    // only while the once-per-run pass they hang off can still run — otherwise no every-step firing
    // can follow and listing one is as misleading as listing the spine.
    var everyStepItems = schedule.EveryStepCanStillRun
      ? entries.Where(e => e.ScheduleType == ScheduleType.EveryStep)
        .Select(e => NewItem(e.SequenceId, names, ScheduleKind.EveryStep, ReasonFor(ScheduleKind.EveryStep, e),
          expectedAt: null, relativeLabel: null, repeats: Repeats(ScheduleKind.EveryStep, cycling), order: 0))
        .ToList()
      : new List<QueueMonitorItem>();

    // (3) Timed firings — template timers (time-of-day next-eligible; relative anchor+offset), live
    // schedules (exact), and self-reschedule Timer firings (exact) — merged, sorted by ExpectedAt.
    // Times come from the run's own schedule state, so a timer that already fired today resolves to
    // tomorrow, one that is overdue reads as due now, and a spent one-shot relative timer is dropped.
    var timed = new List<QueueMonitorItem>();
    for (var i = 0; i < entries.Count; i++) {
      var e = entries[i];
      if (e.ScheduleType != ScheduleType.Timer) continue;
      if (e.TimerTimeOfDay is { } tod) {
        var dueAt = schedule.NextTimeOfDayDue(i, tod, now);
        timed.Add(NewItem(e.SequenceId, names, ScheduleKind.TimerTimeOfDay, $"At {tod:HH:mm}",
          dueAt, relativeLabel: dueAt <= now ? "due" : null, repeats: false, order: 0));
      }
      else if (e.TimerRelativeOffset is { } offset) {
        // Fires at most once per run — once fired it is gone, not "due" forever.
        if (schedule.RelativeFired(i)) continue;
        var expectedAt = schedule.RunStartedAt + offset;
        var elapsed = expectedAt <= now;
        timed.Add(NewItem(e.SequenceId, names, ScheduleKind.TimerRelative, $"+{offset} after start",
          expectedAt, relativeLabel: elapsed ? "due" : null, repeats: false, order: 0));
      }
    }
    foreach (var kv in liveSchedules) {
      timed.Add(NewItem(kv.Key, names, ScheduleKind.LiveSchedule, "Scheduled live",
        kv.Value, relativeLabel: null, repeats: false, order: 0));
    }
    foreach (var firing in selfTimerFirings.Where(f => f.FireAt is not null)) {
      timed.Add(NewItem(firing.SequenceId, names, ScheduleKind.SelfReschedule, "Rescheduled by a sequence",
        firing.FireAt, relativeLabel: null, repeats: false, order: 0));
    }
    timed = timed.OrderBy(i => i.ExpectedAt).ToList();

    // Idle-but-alive (FR-009): with no imminent once-per-run step but a pending future firing, label
    // the earliest timed item "waiting" so the run reads as alive rather than empty/stuck.
    if (spineItems.Count == 0 && timed.Count > 0 && timed[0].RelativeLabel is null) {
      timed[0] = timed[0] with { RelativeLabel = "waiting" };
    }

    // Assign stable order across the whole list: spine → every-step → sorted timed.
    return spineItems.Concat(everyStepItems).Concat(timed)
      .Select((item, i) => item with { Order = i }).ToList();
  }

  // ── Last outcome (not running) ──────────────────────────────────────────────────────────────

  private async Task<RunOutcome?> BuildLastOutcomeAsync(string queueId, CancellationToken ct) {
    var page = await _log.QueryAsync(
      new ExecutionLogQuery { ObjectType = "queue", ObjectId = queueId, RootsOnly = true, PageSize = 20 }, ct)
      .ConfigureAwait(false);
    // Default sort is timestamp descending → the first finalized queue-run entry is the latest one.
    var latest = page.Items.FirstOrDefault(e =>
      string.Equals(e.ObjectRef.ObjectType, "queue", StringComparison.OrdinalIgnoreCase)
      && string.Equals(e.ObjectRef.ObjectId, queueId, StringComparison.Ordinal)
      && !string.Equals(e.FinalStatus, "running", StringComparison.OrdinalIgnoreCase));
    return latest is null ? null : new RunOutcome(latest.FinalStatus, latest.Summary);
  }

  // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

  private async Task<IReadOnlyDictionary<string, string>> ResolveSequenceNamesAsync() {
    var all = await _sequences.ListAsync().ConfigureAwait(false);
    return all.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
  }

  private static QueueMonitorItem NewItem(
    string sequenceId, IReadOnlyDictionary<string, string> names, ScheduleKind kind, string reason,
    DateTimeOffset? expectedAt, string? relativeLabel, bool repeats, int order) {
    var resolved = names.TryGetValue(sequenceId, out var n) ? n : null;
    return new QueueMonitorItem(sequenceId, resolved, Stale: resolved is null, kind, reason,
      expectedAt, relativeLabel, repeats, order);
  }

  private static ScheduleKind KindFor(QueueTemplateEntry entry) => entry.ScheduleType switch {
    ScheduleType.AtQueueStart => ScheduleKind.AtQueueStart,
    ScheduleType.EveryStep => ScheduleKind.EveryStep,
    ScheduleType.Timer => entry.TimerTimeOfDay is not null ? ScheduleKind.TimerTimeOfDay : ScheduleKind.TimerRelative,
    _ => ScheduleKind.OncePerRun
  };

  private static string ReasonFor(ScheduleKind kind, QueueTemplateEntry? entry) => kind switch {
    ScheduleKind.AtQueueStart => "At queue start",
    ScheduleKind.EveryStep => "After Every Step",
    ScheduleKind.TimerTimeOfDay => entry?.TimerTimeOfDay is { } tod ? $"At {tod:HH:mm}" : "Timed",
    ScheduleKind.TimerRelative => entry?.TimerRelativeOffset is { } off ? $"+{off} after start" : "Timed",
    ScheduleKind.LiveSchedule => "Scheduled live",
    ScheduleKind.SelfReschedule => "Rescheduled by a sequence",
    _ => "Once per run"
  };

  // OncePerRun/EveryStep recur every cycle on a cycling queue; timed/live/self-reschedule fire once.
  private static bool Repeats(ScheduleKind kind, bool cycling) =>
    cycling && kind is ScheduleKind.OncePerRun or ScheduleKind.EveryStep;
}
