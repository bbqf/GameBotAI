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

  /// <summary>
  /// Executed before each timed, live, or self-rescheduled firing (feature 188); the monitor shows it
  /// one time as "Before Each Run" (feature 095).
  /// </summary>
  BeforeEachRun,

  /// <summary>Template Timer in time-of-day mode — fires at a wall-clock <c>HH:mm</c>.</summary>
  TimerTimeOfDay,

  /// <summary>Template Timer in relative mode — fires at <c>RunStartedAt + offset</c>.</summary>
  TimerRelative,

  /// <summary>Ephemeral live schedule (feature 059) — fires at an exact instant.</summary>
  LiveSchedule,

  /// <summary>Ephemeral self-reschedule Timer firing (feature 065) — fires at an exact instant.</summary>
  SelfReschedule,

  /// <summary>
  /// Transient idle-pause state (feature 073) — the game is backed out while the run waits for the
  /// next firing. Not a real sequence and never written to the execution log; surfaced only as the
  /// monitor's current item, with <c>ExpectedAt</c> = the resume instant.
  /// </summary>
  IdlePause,

  /// <summary>A "run next" request (feature 184) — fires once, right after the running sequence.</summary>
  RunNext
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
  RunOutcome? LastOutcome,
  /// <summary>
  /// The emulator this run exclusively holds (feature 079, FR-018); null when not running. Read from
  /// the run's own handle so several concurrent runs each report their own device.
  /// </summary>
  string? DeviceSerial = null,
  /// <summary>
  /// The state of each enabled entry that has an ID (feature 184): <c>running</c>, <c>queued</c>, <c>scheduled</c>
  /// or <c>idle</c>. Empty when the queue does not run.
  /// </summary>
  IReadOnlyDictionary<string, string>? EntryStates = null,
  /// <summary>
  /// The smart scheduling settings of the stored queue (feature 189), for a running queue and for a stopped queue.
  /// Null only in a snapshot that a test builds by hand; the projection then shows off and 15.
  /// </summary>
  QueueMonitorSmartScheduling? SmartScheduling = null);

/// <summary>The smart scheduling settings of the stored queue, in the monitor (feature 189).</summary>
/// <param name="Enabled">True when the stored queue has smart scheduling on.</param>
/// <param name="WindowMinutes">The window in minutes.</param>
internal sealed record QueueMonitorSmartScheduling(bool Enabled, int WindowMinutes);

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
  int Order,
  /// <summary>
  /// The requested time of a self-reschedule firing that smart scheduling postponed (feature 189).
  /// <c>ExpectedAt</c> is then the booked time. Null for all other items.
  /// </summary>
  DateTimeOffset? RequestedAt = null);

/// <summary>Best-effort last finalized run outcome (shown when the queue is not running).</summary>
internal sealed record RunOutcome(string Status, string Summary);
