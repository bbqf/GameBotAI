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
  }
}
