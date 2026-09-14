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
    Failure,

    /// <summary>
    /// The queue's failure policy ended the run after its consecutive-failed-cycle threshold was
    /// reached (feature 087). Deliberately distinct from <see cref="StoppedManually"/> — reusing
    /// that would tell an operator a person halted production when nobody did — and from
    /// <see cref="Failure"/>, which means the run itself could not proceed (no template, no device).
    /// Here the run was working fine; its <i>content</i> kept failing.
    /// <para>Appended at the end of the enum so existing numeric values are unchanged.</para>
    /// </summary>
    StoppedByFailurePolicy
  }
}
