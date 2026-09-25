using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Queues;

/// <summary>
/// One completed run of one sequence in one queue (feature 105). A run in progress has no record.
/// </summary>
public sealed record SequenceRunRecord {
  /// <summary>The service-local time, with offset, just before the sequence started.</summary>
  public DateTimeOffset StartedAt { get; init; }

  /// <summary>The service-local time, with offset, when the sequence ended.</summary>
  public DateTimeOffset EndedAt { get; init; }

  /// <summary>The status of the run.</summary>
  public SequenceRunStatus Status { get; init; }
}

/// <summary>
/// The run statistics of one (queue, sequence) pair (feature 105): the last-run values, the total
/// counts, and the <see cref="MaxRecentRuns"/> most recent run records, oldest first.
/// </summary>
public sealed class SequenceRunStatistics {
  /// <summary>
  /// The maximum number of run records that the entry keeps. When a new record makes the count more
  /// than this value, the oldest record is removed. The counters do not decrease.
  /// </summary>
  public const int MaxRecentRuns = 100;

  /// <summary>The start of the newest record. Null before the first record.</summary>
  public DateTimeOffset? LastRunStartedAt { get; set; }

  /// <summary>The end of the newest record. Null before the first record.</summary>
  public DateTimeOffset? LastRunEndedAt { get; set; }

  /// <summary>The status of the newest record. Null before the first record.</summary>
  public SequenceRunStatus? LastRunStatus { get; set; }

  /// <summary>The end of the newest success record. A failure or a cancelled record does not change it.</summary>
  public DateTimeOffset? LastSuccessAt { get; set; }

  /// <summary>The total number of success records since the first record.</summary>
  public long SuccessCount { get; set; }

  /// <summary>The total number of failure records since the first record.</summary>
  public long FailureCount { get; set; }

  /// <summary>The total number of cancelled records since the first record.</summary>
  public long CancelledCount { get; set; }

  /// <summary>The most recent run records, oldest first. At most <see cref="MaxRecentRuns"/> records.</summary>
  [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
  public Collection<SequenceRunRecord> RecentRuns { get; } = new();

  /// <summary>
  /// Adds <paramref name="record"/> as the newest record: updates the last-run values and the counter
  /// of its status, and removes the oldest records when there are more than <see cref="MaxRecentRuns"/>.
  /// </summary>
  public void Apply(SequenceRunRecord record) {
    ArgumentNullException.ThrowIfNull(record);

    RecentRuns.Add(record);
    while (RecentRuns.Count > MaxRecentRuns) {
      RecentRuns.RemoveAt(0);
    }

    LastRunStartedAt = record.StartedAt;
    LastRunEndedAt = record.EndedAt;
    LastRunStatus = record.Status;

    switch (record.Status) {
      case SequenceRunStatus.Success:
        SuccessCount++;
        LastSuccessAt = record.EndedAt;
        break;
      case SequenceRunStatus.Failure:
        FailureCount++;
        break;
      case SequenceRunStatus.Cancelled:
        CancelledCount++;
        break;
    }
  }

  /// <summary>
  /// True when a kept record has the status <paramref name="status"/> and its end time is in the
  /// window. Both ends of the window are included: <c>from &lt;= EndedAt &lt;= to</c>.
  /// </summary>
  public bool HasRunInWindow(SequenceRunStatus status, DateTimeOffset from, DateTimeOffset to) =>
    RecentRuns.Any(r => r.Status == status && r.EndedAt >= from && r.EndedAt <= to);

  /// <summary>Returns a deep copy, so that a caller cannot change the store cache.</summary>
  public SequenceRunStatistics Clone() {
    var copy = new SequenceRunStatistics {
      LastRunStartedAt = LastRunStartedAt,
      LastRunEndedAt = LastRunEndedAt,
      LastRunStatus = LastRunStatus,
      LastSuccessAt = LastSuccessAt,
      SuccessCount = SuccessCount,
      FailureCount = FailureCount,
      CancelledCount = CancelledCount
    };
    // The records are immutable, so a copy of the list is a deep copy.
    foreach (var record in RecentRuns) copy.RecentRuns.Add(record);
    return copy;
  }
}
