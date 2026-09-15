namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request/response shape of a queue's failure policy (feature 087, issue #181).
  /// <para>
  /// Field names deliberately diverge from the originating issue's informal sketch
  /// (<c>onConsecutiveFailures: { count }</c>): <c>count</c> does not say what it counts when read
  /// back off a stored queue a year later, and the object needed a third field the sketch did not
  /// anticipate. Recorded as research decision R11 in <c>specs/087-queue-failure-policy</c>.
  /// </para>
  /// <para>Carries no credential — the auth header lives in service configuration only.</para>
  /// </summary>
  internal sealed class QueueFailurePolicyDto {
    /// <summary>Consecutive failed cycles before the policy trips. MUST be at least 1.</summary>
    public int ConsecutiveFailedCycles { get; set; }

    /// <summary>
    /// One of <c>notify</c>, <c>stop</c>, <c>pause</c>, <c>notifyAndStop</c> (case-insensitive on
    /// input). A value outside that set is rejected with 400 rather than silently defaulting.
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// Optional destination overriding <c>Service:Notifications:DefaultUrl</c>. When supplied MUST
    /// be an absolute http or https URL.
    /// </summary>
    public string? NotifyUrl { get; set; }
  }
}
