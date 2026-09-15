namespace GameBot.Service.Services.Notifications {
  /// <summary>
  /// Service-wide configuration for outbound failure notifications (feature 087, issue #181).
  /// Bound from the <c>Service:Notifications</c> configuration section.
  /// <para>
  /// A queue's own <see cref="GameBot.Domain.Queues.QueueFailurePolicy.NotifyUrl"/> overrides
  /// <see cref="DefaultUrl"/>; everything else here applies to every delivery.
  /// </para>
  /// </summary>
  internal sealed class FailureNotificationOptions {
    public const string SectionName = "Service:Notifications";

    /// <summary>Per-attempt timeout in seconds when the configured value is absent or out of range.</summary>
    public const int DefaultTimeoutSeconds = 5;

    /// <summary>Total attempts (not retries) when the configured value is absent or out of range.</summary>
    public const int DefaultMaxAttempts = 2;

    /// <summary>
    /// Destination every notifying queue uses unless its policy names its own. Null means no
    /// service-wide default, in which case a notifying policy MUST supply a URL or be rejected.
    /// </summary>
    public string? DefaultUrl { get; set; }

    /// <summary>
    /// Optional static authentication header name (for example <c>X-GameBot-Token</c>), so a
    /// receiver off this machine can verify the caller. Null disables authentication.
    /// </summary>
    public string? AuthHeaderName { get; set; }

    /// <summary>
    /// The value sent for <see cref="AuthHeaderName"/>. <b>Secret.</b> It is never returned by any
    /// endpoint, never written to a log, and never copied into a response DTO.
    /// </summary>
    public string? AuthHeaderValue { get; set; }

    /// <summary>Per-attempt timeout. Values below 1 are coerced to <see cref="DefaultTimeoutSeconds"/>.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>
    /// Total attempts per notification, including the first. Values below 1 are coerced to
    /// <see cref="DefaultMaxAttempts"/>; the delivery is bounded so a black-hole receiver cannot
    /// keep a notification alive indefinitely.
    /// </summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>Effective per-attempt timeout, with out-of-range values coerced to the default.</summary>
    public int EffectiveTimeoutSeconds => TimeoutSeconds >= 1 ? TimeoutSeconds : DefaultTimeoutSeconds;

    /// <summary>Effective attempt count, with out-of-range values coerced to the default.</summary>
    public int EffectiveMaxAttempts => MaxAttempts >= 1 ? MaxAttempts : DefaultMaxAttempts;

    /// <summary>True when both an auth header name and value are configured.</summary>
    public bool HasAuthHeader =>
      !string.IsNullOrWhiteSpace(AuthHeaderName) && !string.IsNullOrWhiteSpace(AuthHeaderValue);
  }
}
