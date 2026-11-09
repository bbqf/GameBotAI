using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>Result of attempting to start a queue run.</summary>
internal enum QueueStartOutcome {
  /// <summary>A background run was launched; the queue is now Running.</summary>
  Started,

  /// <summary>No queue exists with the given id.</summary>
  NotFound,

  /// <summary>A run is already in progress for this queue (051 FR-013a).</summary>
  AlreadyRunning,

  /// <summary>
  /// The queue's bound emulator is claimed by a <i>different</i> running queue (feature 079,
  /// FR-009/FR-010). Deliberately distinct from <see cref="AlreadyRunning"/> so the operator can tell
  /// "this queue is running" from "this queue's device is busy".
  /// </summary>
  DeviceInUse
}

/// <summary>Result of attempting to register a live relative schedule (feature 059).</summary>
internal enum LiveScheduleOutcome {
  /// <summary>The schedule was registered against the active run.</summary>
  Scheduled,

  /// <summary>The queue has no active run, so there is nothing to schedule against (FR-007).</summary>
  NotRunning
}

/// <summary>Outcome plus the resolved expected fire instant of a live relative schedule.</summary>
internal readonly record struct LiveScheduleResult(LiveScheduleOutcome Outcome, DateTimeOffset ExpectedFireAt);

/// <summary>Result of a run-next request (feature 184).</summary>
internal enum RunNextOutcome {
  /// <summary>The request was added. The entry runs right after the running sequence.</summary>
  Queued,

  /// <summary>A request for the entry already waited. Nothing was added.</summary>
  AlreadyQueued,

  /// <summary>The queue has no active run.</summary>
  NotRunning,

  /// <summary>The entry is not in the plan of the run, it has no ID, or it is disabled.</summary>
  EntryNotAvailable,

  /// <summary>The sequence of the entry runs now.</summary>
  EntryRunning,

  /// <summary>The run still has a normal firing for the entry.</summary>
  EntryScheduled
}

/// <summary>Outcome of a run-next request plus the sequence of the entry (empty when the entry was not found).</summary>
internal readonly record struct RunNextResult(RunNextOutcome Outcome, string SequenceId = "");

/// <summary>
/// Owns the lifecycle of live queue runs: launching a background run on start, cancelling it on
/// stop, and tracking which queues are currently running. One run at a time per queue (051 FR-013a),
/// and — since feature 079 — one run at a time per emulator: queues bound to <i>different</i> devices
/// run concurrently and independently, while a start whose device is already claimed is refused with
/// <see cref="QueueStartOutcome.DeviceInUse"/>. This reverses 051 FR-013, which allowed two runs to
/// share one emulator and left the resulting interference to the operator.
/// </summary>
internal interface IQueueExecutionService {
  /// <summary>
  /// Starts a background run for the queue. Returns immediately; the run proceeds asynchronously
  /// and records its outcome in the execution log. Returns <see cref="QueueStartOutcome.AlreadyRunning"/>
  /// without launching a second run when one is already in progress.
  /// </summary>
  Task<QueueStartOutcome> StartAsync(string queueId, CancellationToken ct = default);

  /// <summary>
  /// Requests prompt cancellation of the queue's in-flight run and waits for it to settle
  /// (disconnect + terminating log entry). A no-op when the queue is not running.
  /// </summary>
  Task StopAsync(string queueId, CancellationToken ct = default);

  /// <summary>True iff a run is currently in progress for the queue.</summary>
  bool IsRunning(string queueId);

  /// <summary>
  /// Registers a live, ephemeral relative schedule against the queue's active run (feature 059):
  /// the sequence fires once at the first iteration boundary at or after <c>now + offset</c>, then
  /// clears. Re-issuing for the same sequence replaces a still-pending schedule (most-recent-wins,
  /// FR-011). The target may be any sequence in the library. Returns
  /// <see cref="LiveScheduleOutcome.NotRunning"/> when the queue has no active run.
  /// </summary>
  LiveScheduleResult ScheduleRelative(string queueId, string sequenceId, TimeSpan offset);

  /// <summary>
  /// Queues one run of the template entry <paramref name="entryId"/> against the active run (feature 184). The entry
  /// runs once, right after the running sequence, or at once when none runs. It runs with the parameter scope of the
  /// entry. The call is safe to repeat: a second call for a queued entry adds nothing and returns
  /// <see cref="RunNextOutcome.AlreadyQueued"/>. The call refuses an entry that is not idle (running or scheduled),
  /// an entry that is not in the plan, and a queue with no active run.
  /// </summary>
  RunNextResult RunNext(string queueId, string entryId);
}
