namespace GameBot.Domain.Commands;

/// <summary>
/// The time bound a queue applies to one sequence firing (feature 094). One source for the number the
/// engine enforces and the number the API publishes, so the two cannot drift apart.
/// </summary>
public static class SequenceTimeLimits {
  /// <summary>Bound applied to a firing whose sequence sets no <see cref="CommandSequence.WatchdogTimeoutMs"/>: 4 minutes.</summary>
  public const int DefaultWatchdogTimeoutMs = 4 * 60 * 1000;

  /// <summary>Largest per-sequence override a sequence may store: 30 minutes.</summary>
  public const int MaxWatchdogTimeoutMs = 30 * 60 * 1000;

  /// <summary>
  /// The bound that actually applies for a stored override.
  /// </summary>
  /// <param name="overrideMs">The sequence's stored <c>WatchdogTimeoutMs</c>, or null when it sets none.</param>
  /// <returns>The override when it is positive; otherwise <see cref="DefaultWatchdogTimeoutMs"/>.</returns>
  public static int Resolve(int? overrideMs) => overrideMs is > 0 and var ms ? ms : DefaultWatchdogTimeoutMs;

  /// <summary>Value of <c>onTimeLimit</c> that gives the status failure at the time limit. This is the default.</summary>
  public const string OnTimeLimitFail = "fail";

  /// <summary>Value of <c>onTimeLimit</c> that ends the run with the status success at the time limit.</summary>
  public const string OnTimeLimitEnd = "end";

  /// <summary>The allowed values of <c>onTimeLimit</c>.</summary>
  public static readonly IReadOnlyList<string> OnTimeLimitValues = new[] { OnTimeLimitFail, OnTimeLimitEnd };

  /// <summary>The error text for an unknown <c>onTimeLimit</c> value. The API and the repository use this one text.</summary>
  public const string OnTimeLimitInvalidMessage = "onTimeLimit must be one of: fail, end.";

  /// <summary>
  /// Checks a stored <c>onTimeLimit</c> value. The check is case-sensitive.
  /// </summary>
  /// <param name="value">The value to check. Null means the member is absent.</param>
  /// <returns>True when the value is null, <c>fail</c>, or <c>end</c>.</returns>
  public static bool IsValidOnTimeLimit(string? value) =>
    value is null || value == OnTimeLimitFail || value == OnTimeLimitEnd;
}
