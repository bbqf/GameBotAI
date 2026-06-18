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

  /// <summary>A run is already in progress for this queue (FR-013a).</summary>
  AlreadyRunning
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

/// <summary>
/// Owns the lifecycle of live queue runs: launching a background run on start, cancelling it on
/// stop, and tracking which queues are currently running. One run at a time per queue; concurrent
/// runs across different queues (including ones bound to the same emulator) are allowed (FR-013).
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
}
