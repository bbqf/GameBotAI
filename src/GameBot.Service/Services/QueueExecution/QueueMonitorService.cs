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
/// <see cref="QueueRunHandle"/> (via <see cref="IQueueRunRegistry"/>) and the linked
/// <see cref="QueueTemplate"/>, folds them with the current local wall-clock into an ordered
/// now/up-next snapshot, and — when the queue is not running — surfaces the best-effort last
/// finalized run outcome from the execution log. No side effects; mirrors, but never duplicates,
/// the run loop's schedule semantics (feature 072).
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

    // NothingScheduled (FR-011): running with no schedulable work of ANY kind — no template entries,
    // no pending live schedules, and no pending self-reschedule timer firings. Computed up front so it
    // stays correct even for a live-scheduled-only run (which must NOT read as "nothing scheduled").
    var liveSchedules = handle.PendingLiveSchedules.ToArray();
    var selfTimerFirings = handle.SnapshotPendingTimerFirings();
    var hasTemplateWork = template is not null && template.Entries.Count > 0;
    var nothingScheduled = !hasTemplateWork && liveSchedules.Length == 0 && selfTimerFirings.Count == 0;

    var current = BuildCurrent(handle, template, names, cycling);
    var upcoming = BuildUpcoming(handle, template, names, now, cycling, current, liveSchedules, selfTimerFirings);

    return new QueueMonitorSnapshot(
      queueId, name, Running: true, CycleExecution: cycling, RunStartedAt: handle.RunStartedAt,
      Current: current, Upcoming: upcoming, NothingScheduled: nothingScheduled, LastOutcome: null);
  }

  // ── Current ("now") ────────────────────────────────────────────────────────────────────────

  private static QueueMonitorItem? BuildCurrent(
    QueueRunHandle handle, QueueTemplate? template, IReadOnlyDictionary<string, string> names, bool cycling) {
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
    var entry = template?.Entries.FirstOrDefault(e => string.Equals(e.SequenceId, sequenceId, StringComparison.Ordinal));
    var kind = entry is null ? ScheduleKind.OncePerRun : KindFor(entry);
    return NewItem(sequenceId, names, kind, ReasonFor(kind, entry), expectedAt: null,
      relativeLabel: "now", repeats: Repeats(kind, cycling), order: 0);
  }

  // ── Upcoming ("up next") ──────────────────────────────────────────────────────────────────

  private static List<QueueMonitorItem> BuildUpcoming(
    QueueRunHandle handle, QueueTemplate? template, IReadOnlyDictionary<string, string> names,
    DateTimeOffset now, bool cycling, QueueMonitorItem? current,
    KeyValuePair<string, DateTimeOffset>[] liveSchedules, IReadOnlyList<SelfRescheduleEntry> selfTimerFirings) {
    var entries = template?.Entries ?? new System.Collections.ObjectModel.Collection<QueueTemplateEntry>();

    // (1) OncePerRun spine in template order — the "playlist" backbone. AtQueueStart is omitted
    // (already ran); the currently-executing once-per-run step is excluded (Current holds it).
    var spine = entries.Where(e => e.ScheduleType == ScheduleType.OncePerRun).ToList();
    if (current is not null) {
      var idx = spine.FindIndex(e => string.Equals(e.SequenceId, current.SequenceId, StringComparison.Ordinal));
      if (idx >= 0) spine.RemoveAt(idx);
    }
    var spineItems = spine.Select((e, i) => NewItem(
      e.SequenceId, names, ScheduleKind.OncePerRun, ReasonFor(ScheduleKind.OncePerRun, e),
      expectedAt: null, relativeLabel: i == 0 ? "next" : "up next",
      repeats: Repeats(ScheduleKind.OncePerRun, cycling), order: 0)).ToList();

    // (2) EveryStep entries, surfaced once each as "After Every Step" (not interleaved per step).
    var everyStepItems = entries.Where(e => e.ScheduleType == ScheduleType.EveryStep)
      .Select(e => NewItem(e.SequenceId, names, ScheduleKind.EveryStep, ReasonFor(ScheduleKind.EveryStep, e),
        expectedAt: null, relativeLabel: null, repeats: Repeats(ScheduleKind.EveryStep, cycling), order: 0))
      .ToList();

    // (3) Timed firings — template timers (time-of-day next-eligible; relative anchor+offset), live
    // schedules (exact), and self-reschedule Timer firings (exact) — merged, sorted by ExpectedAt.
    var timed = new List<QueueMonitorItem>();
    foreach (var e in entries.Where(e => e.ScheduleType == ScheduleType.Timer)) {
      if (e.TimerTimeOfDay is { } tod) {
        timed.Add(NewItem(e.SequenceId, names, ScheduleKind.TimerTimeOfDay, $"At {tod:HH:mm}",
          NextEligible(now, tod), relativeLabel: null, repeats: false, order: 0));
      }
      else if (e.TimerRelativeOffset is { } offset) {
        var expectedAt = handle.RunStartedAt + offset;
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

  // Next-eligible wall-clock for a time-of-day timer: today at HH:mm if still ahead, else tomorrow.
  private static DateTimeOffset NextEligible(DateTimeOffset now, TimeOnly tod) {
    var todayAt = new DateTimeOffset(now.Year, now.Month, now.Day, tod.Hour, tod.Minute, tod.Second, now.Offset);
    return now.TimeOfDay < tod.ToTimeSpan() ? todayAt : todayAt.AddDays(1);
  }
}
