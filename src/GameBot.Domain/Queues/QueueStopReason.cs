namespace GameBot.Domain.Queues {
  /// <summary>
  /// Why a queue run terminated. Recorded on the queue-run execution log entry so an
  /// operator can see how the run ended.
  /// </summary>
  public enum QueueStopReason {
    /// <summary>Ran all of the template's sequences once (cycle off), or the template was empty.</summary>
    CompletedFullRun,

    /// <summary>The operator stopped the run via the UI or API (including while cycling).</summary>
    StoppedManually,

    /// <summary>
    /// A run-level failure ended the run: no resolvable linked template, the bound emulator
    /// could not be reached at start, or the emulator connection was lost mid-run. Individual
    /// per-sequence failures are NOT run-level failures and do not produce this reason.
    /// </summary>
    Failure
  }
}
