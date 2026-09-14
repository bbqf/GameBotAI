using System;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Live health of a queue's current run (feature 086, issue #180) — enough to tell a queue that is
  /// cycling healthily from one that is failing every cycle, without stopping it.
  /// <para>
  /// Attached to <see cref="QueueDetailResponse.Health"/> and present <b>only</b> when that same
  /// response reports the queue as Running; null otherwise, never a zeroed object, so a stopped queue
  /// can never be misread as a live one that has completed no cycles.
  /// </para>
  /// <para>All values describe the current run; a restarted queue begins again at zero.</para>
  /// </summary>
  internal sealed class QueueHealthResponse {
    /// <summary>When the current run started (local clock).</summary>
    public DateTimeOffset? RunStartedAt { get; set; }

    /// <summary>Cycles completed in the current run. Keeps counting past the retention bound.</summary>
    public int CyclesCompleted { get; set; }

    /// <summary>Start of the most recent completed cycle; null until one completes.</summary>
    public DateTimeOffset? LastCycleStartedAt { get; set; }

    /// <summary>Completion of the most recent completed cycle; null until one completes.</summary>
    public DateTimeOffset? LastCycleCompletedAt { get; set; }

    /// <summary>
    /// "success" or "failure" for the most recent completed cycle — the execution log's existing
    /// status vocabulary, not a new boolean one. Null until a cycle completes, together with the two
    /// timestamps above.
    /// </summary>
    public string? LastCycleStatus { get; set; }

    /// <summary>
    /// How many most-recent cycles failed in a row; reset to zero by a successful cycle. This is the
    /// value that makes a silently-failing queue visible. Exposed only — nothing acts on it.
    /// </summary>
    public int ConsecutiveFailedCycles { get; set; }

    /// <summary>Roster position being executed; null outside the once-per-run pass.</summary>
    public int? CurrentEntryIndex { get; set; }

    /// <summary>Sequence executing right now; null between firings.</summary>
    public string? CurrentSequenceId { get; set; }
  }
}
