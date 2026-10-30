using GameBot.Domain.Queues;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Result of <c>POST /api/queues/{id}/resume</c> (feature 087, issue #181).
  /// <para>
  /// Every known-queue case answers 200 with <see cref="Resumed"/> saying whether anything actually
  /// changed — the same "render a state, don't handle an error" contract as <c>{id}/monitor</c> and
  /// <c>{id}/cycles</c>. Only an unknown queue is a 404.
  /// </para>
  /// </summary>
  internal sealed class QueueResumeResponse {
    /// <summary>The queue's id, echoed so a caller batching requests can correlate.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The queue's execution status. A paused run is <c>Running</c> both before and after a resume:
    /// a policy pause is run state, not a third status value.
    /// </summary>
    public QueueExecutionStatus Status { get; set; }

    /// <summary>
    /// True when a policy pause was actually released. False when the queue was not running, or was
    /// running but not paused — neither of which is an error.
    /// </summary>
    public bool Resumed { get; set; }

    /// <summary>
    /// Which pause the call released: <c>operator</c> (an operator pause, or the cancel of its request) or
    /// <c>failurePolicy</c>. Null when nothing changed. When both are in force, the first call releases the
    /// operator pause and a second call releases the failure-policy pause (feature 171).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }
  }

  /// <summary>
  /// Result of <c>POST /api/queues/{id}/pause</c> (feature 171).
  /// </summary>
  internal sealed class QueuePauseResponse {
    /// <summary>The queue's id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// <c>pausing</c> when a sequence still runs or the loop has not reached its gate yet, or <c>paused</c> when the queue
    /// waits between sequences.
    /// </summary>
    public string OperatorPause { get; set; } = "pausing";

    /// <summary>When the pause was requested (service-local clock).</summary>
    public System.DateTimeOffset? RequestedAt { get; set; }
  }
}
