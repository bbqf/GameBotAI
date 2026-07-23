using System;
using System.Collections.ObjectModel;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Read-only snapshot of a queue's live run plan for the monitor panel: the sequence running now,
  /// the ordered up-next list, and (when not running) the best-effort last outcome. Serialized
  /// camelCase; <c>scheduleKind</c> as a string enum. Returned by <c>GET {id}/monitor</c>.
  /// </summary>
  internal sealed class QueueMonitorResponse {
    public string QueueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether a run is currently registered for the queue.</summary>
    public bool Running { get; set; }

    /// <summary>Queue repeats its once-per-run steps each cycle (drives the "repeats" marker).</summary>
    public bool CycleExecution { get; set; }

    /// <summary>Local-clock instant the run loop started (anchor for relative timers); null when not running.</summary>
    public DateTimeOffset? RunStartedAt { get; set; }

    /// <summary>The sequence executing right now; null when idle or not running.</summary>
    public QueueMonitorItemResponse? Current { get; set; }

    /// <summary>Ordered next items (once-per-run spine → every-step → timed/live/self-reschedule).</summary>
    public Collection<QueueMonitorItemResponse> Upcoming { get; } = new();

    /// <summary>Running but nothing to execute (no template/entries and no pending firings).</summary>
    public bool NothingScheduled { get; set; }

    /// <summary>Best-effort last finalized run outcome when <see cref="Running"/> is false; null otherwise.</summary>
    public RunOutcomeResponse? LastOutcome { get; set; }
  }

  /// <summary>A single now/up-next monitor item (camelCase wire shape).</summary>
  internal sealed class QueueMonitorItemResponse {
    public string SequenceId { get; set; } = string.Empty;

    /// <summary>Resolved sequence display name; null when the reference is stale/unresolved.</summary>
    public string? SequenceName { get; set; }

    /// <summary>True when the referenced sequence can no longer be resolved.</summary>
    public bool Stale { get; set; }

    /// <summary>One of AtQueueStart/OncePerRun/EveryStep/TimerTimeOfDay/TimerRelative/LiveSchedule/SelfReschedule.</summary>
    public string ScheduleKind { get; set; } = string.Empty;

    /// <summary>Human-readable schedule reason for display.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Absolute expected time when known; null for spine steps with no wall-clock time.</summary>
    public DateTimeOffset? ExpectedAt { get; set; }

    /// <summary>Hint when there is no absolute time: now / next / up next / waiting.</summary>
    public string? RelativeLabel { get; set; }

    /// <summary>Item recurs every cycle (cycling queues).</summary>
    public bool Repeats { get; set; }

    /// <summary>Stable position within the list.</summary>
    public int Order { get; set; }
  }

  /// <summary>Best-effort last finalized run outcome for a stopped queue.</summary>
  internal sealed class RunOutcomeResponse {
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
  }
}
