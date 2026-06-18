using System;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request body for <c>POST /api/queues/{id}/live-schedule</c> (feature 059): schedule a sequence
  /// to fire once after a relative offset from now against the queue's active run.
  /// </summary>
  internal sealed class LiveScheduleRequest {
    /// <summary>Any sequence id in the library. Must be non-blank.</summary>
    public string? SequenceId { get; set; }

    /// <summary>Relative offset from now as an "HH:mm:ss" duration (e.g. "00:10:00").</summary>
    public string? Offset { get; set; }
  }

  /// <summary>Response body for a successful live relative schedule.</summary>
  internal sealed class LiveScheduleResponse {
    public string SequenceId { get; set; } = string.Empty;
    public string Offset { get; set; } = string.Empty;

    /// <summary>
    /// Call time + offset (server local clock). The actual firing occurs at the first iteration
    /// boundary at or after this instant.
    /// </summary>
    public DateTimeOffset ExpectedFireAt { get; set; }
  }
}
