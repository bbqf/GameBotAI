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

    // ── Failure policy (feature 087, issue #181) ───────────────────────────────────────────────
    // Feature 086 exposed ConsecutiveFailedCycles and had nothing act on it. These fields show what
    // is now acting on it, so an operator can see the policy's state without waiting for it to fire.

    /// <summary>Whether this queue has a failure policy configured at all.</summary>
    public bool FailurePolicyConfigured { get; set; }

    /// <summary>
    /// Whether the policy has tripped for the current failure episode. Cleared by a successful
    /// cycle, so it reads false again once the queue recovers.
    /// </summary>
    public bool FailurePolicyTripped { get; set; }

    // ── Pause (features 087 + 096, issue #199) ─────────────────────────────────────────────────
    // One set of fields for both kinds of pause, so "is this farm paused?" is a single read.

    /// <summary>
    /// Whether the run is paused right now, for either reason: an idle pause (feature 073; routine,
    /// ends by itself when the next firing is due) or a failure-policy pause (feature 087; released
    /// only by <c>POST /api/queues/{id}/resume</c>). <see cref="PauseKind"/> tells them apart.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>When the reported pause began (local clock); null when not paused.</summary>
    public DateTimeOffset? PausedAt { get; set; }

    /// <summary>
    /// Why the run is paused: <c>idle pause: resumes at HH:mm</c>, or a text starting
    /// <c>failure policy:</c>. Null when not paused.
    /// </summary>
    public string? PauseReason { get; set; }

    /// <summary>
    /// <c>idle</c> or <c>failurePolicy</c>; null when not paused. When both pauses are in force the
    /// failure-policy pause is the one reported, since it is the one that needs an operator.
    /// </summary>
    public string? PauseKind { get; set; }

    /// <summary>When the most recent notification attempt finished; null until one is made.</summary>
    public DateTimeOffset? LastNotificationAt { get; set; }

    /// <summary>Whether that attempt succeeded; null until one is made.</summary>
    public bool? LastNotificationSucceeded { get; set; }

    /// <summary>
    /// Why the most recent attempt failed; null on success. This is what lets an operator establish
    /// that an alert did not get out without opening a log file on the host.
    /// <para>
    /// Never contains the configured auth header value or the receiver's response body.
    /// </para>
    /// </summary>
    public string? LastNotificationError { get; set; }
  }
}
