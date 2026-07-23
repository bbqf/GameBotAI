using System;
using System.Collections.Generic;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Why a monitor item is scheduled to run. Mirrors the schedule semantics the run loop executes,
/// mapping template <c>ScheduleType</c> (plus the timer time-of-day/relative split and the two
/// ephemeral run-scoped kinds) to a single operator-facing vocabulary for the live monitor.
/// </summary>
internal enum ScheduleKind {
  /// <summary>Ran once at run start, before timers and the first once-per-run step.</summary>
  AtQueueStart,

  /// <summary>The regular "step" — executed in template order; defines run completion.</summary>
  OncePerRun,

  /// <summary>Executed after each once-per-run step; surfaced once as "After Every Step".</summary>
  EveryStep,

  /// <summary>Template Timer in time-of-day mode — fires at a wall-clock <c>HH:mm</c>.</summary>
  TimerTimeOfDay,

  /// <summary>Template Timer in relative mode — fires at <c>RunStartedAt + offset</c>.</summary>
  TimerRelative,

  /// <summary>Ephemeral live schedule (feature 059) — fires at an exact instant.</summary>
  LiveSchedule,

  /// <summary>Ephemeral self-reschedule Timer firing (feature 065) — fires at an exact instant.</summary>
  SelfReschedule
}

/// <summary>
/// Read-only projection of what a queue's current run is doing now and will do next. A pure function
/// of (linked template, run-handle snapshot, <c>now</c>, best-effort last outcome); never persisted.
/// </summary>
internal sealed record QueueMonitorSnapshot(
  string QueueId,
  string Name,
  bool Running,
  bool CycleExecution,
  DateTimeOffset? RunStartedAt,
  QueueMonitorItem? Current,
  IReadOnlyList<QueueMonitorItem> Upcoming,
  bool NothingScheduled,
  RunOutcome? LastOutcome);

/// <summary>One item in the monitor's now/up-next plan.</summary>
internal sealed record QueueMonitorItem(
  string SequenceId,
  string? SequenceName,
  bool Stale,
  ScheduleKind ScheduleKind,
  string Reason,
  DateTimeOffset? ExpectedAt,
  string? RelativeLabel,
  bool Repeats,
  int Order);

/// <summary>Best-effort last finalized run outcome (shown when the queue is not running).</summary>
internal sealed record RunOutcome(string Status, string Summary);
