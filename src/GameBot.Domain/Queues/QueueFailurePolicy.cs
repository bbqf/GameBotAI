namespace GameBot.Domain.Queues {
  /// <summary>
  /// Optional, persisted instruction for what a queue does when it keeps failing (feature 087,
  /// issue #181). Null on <see cref="ExecutionQueue.FailurePolicy"/> means no policy: nothing is
  /// evaluated and the run behaves exactly as it did before this feature existed.
  /// <para>
  /// Evaluated against the consecutive-failed-cycle count feature 086 already maintains for the
  /// run — a cycle fails when at least one entry in it failed, and only completed cycles count.
  /// This is deliberately <i>cycle</i> granularity, not per-sequence: a single flaky firing must
  /// not trip a policy.
  /// </para>
  /// <para>
  /// <b>Carries no credential by design.</b> The authentication header for a destination lives in
  /// service configuration (<c>Service:Notifications</c>), never here — a queue is persisted to a
  /// JSON file that the backup/restore endpoints copy around, which is no place for a secret.
  /// </para>
  /// </summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1056:URI-like properties should not be strings",
    Justification = "NotifyUrl is persisted configuration that must round-trip byte-for-byte as the "
                  + "operator wrote it. System.Uri normalizes (trailing slashes, escaping, default "
                  + "ports), so a stored value would not read back as written, and deserializing a "
                  + "malformed stored value would throw inside the repository instead of being "
                  + "reported by the API boundary that already validates it.")]
  public class QueueFailurePolicy {
    /// <summary>
    /// How many cycles must fail in a row before the policy trips. MUST be at least 1; a
    /// non-positive value is rejected at the API boundary rather than tripping on every cycle.
    /// <para>
    /// Counted in cycles, not minutes: a roster whose cycle takes three minutes alerts in about
    /// fifteen at a threshold of five, while one that cycles in seconds needs a much larger number.
    /// </para>
    /// </summary>
    public int ConsecutiveFailedCycles { get; set; }

    /// <summary>What to do when the threshold is reached.</summary>
    public QueueFailureAction Action { get; set; }

    /// <summary>
    /// Optional destination overriding the service-wide default. When supplied it MUST be an
    /// absolute http or https URL. Null falls back to <c>Service:Notifications:DefaultUrl</c>; a
    /// notifying action with no destination from either source is rejected when the queue is saved,
    /// because a policy that looks configured and can never fire is a configuration error.
    /// </summary>
    public string? NotifyUrl { get; set; }

    /// <summary>True when this policy's action delivers a notification.</summary>
    public bool Notifies =>
      Action is QueueFailureAction.Notify or QueueFailureAction.NotifyAndStop;

    /// <summary>True when this policy's action ends the run.</summary>
    public bool Stops =>
      Action is QueueFailureAction.Stop or QueueFailureAction.NotifyAndStop;
  }
}
