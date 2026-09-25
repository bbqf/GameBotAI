using System;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// The run statistics of one sequence in one queue (feature 105), as the queue read returns them in
  /// <see cref="QueueDetailResponse.SequenceStats"/>. The recent-run history of the store is not in
  /// the response.
  /// </summary>
  internal sealed class QueueSequenceStatsResponse {
    /// <summary>The name from the sequence store. Null when the sequence no longer exists.</summary>
    public string? SequenceName { get; set; }

    /// <summary>The start of the last completed run (service-local time, with offset).</summary>
    public DateTimeOffset? LastRunStartedAt { get; set; }

    /// <summary>The end of the last completed run.</summary>
    public DateTimeOffset? LastRunEndedAt { get; set; }

    /// <summary>The status of the last completed run: <c>success</c>, <c>failure</c> or <c>cancelled</c>.</summary>
    public string? LastRunStatus { get; set; }

    /// <summary>The end of the last successful run. Null when no run succeeded.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>The total number of successful runs.</summary>
    public long SuccessCount { get; set; }

    /// <summary>The total number of failed runs.</summary>
    public long FailureCount { get; set; }

    /// <summary>The total number of cancelled runs.</summary>
    public long CancelledCount { get; set; }
  }
}
