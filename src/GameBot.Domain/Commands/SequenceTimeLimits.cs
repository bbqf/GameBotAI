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
}
