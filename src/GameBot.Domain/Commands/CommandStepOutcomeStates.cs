namespace GameBot.Domain.Commands;

/// <summary>
/// The states of one step inside a command that a <c>commandStepOutcome</c> condition may name
/// (feature 167). One list serves the validators, the evaluator, and the API text.
/// </summary>
public static class CommandStepOutcomeStates {
  /// <summary>The step ran and did its work.</summary>
  public const string Executed = "executed";

  /// <summary>The step did not run because its image was not found.</summary>
  public const string SkippedDetectionFailed = "skipped_detection_failed";

  /// <summary>The step waited and the wait ended because of the timeout.</summary>
  public const string CompletedTimeout = "completed_timeout";

  /// <summary>The step waited and the image appeared.</summary>
  public const string ImageDetected = "image_detected";

  /// <summary>The allowed states, in the order that the error text shows them.</summary>
  public static IReadOnlyList<string> All { get; } =
    new[] { Executed, SkippedDetectionFailed, CompletedTimeout, ImageDetected };

  /// <summary>The allowed states as one text, for example <c>executed|skipped_detection_failed|...</c>.</summary>
  public static string AllText { get; } = string.Join('|', All);

  private static readonly HashSet<string> AllSet = new(All, StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// True when <paramref name="state"/> is an allowed state (not case-sensitive). The reason code
  /// <c>detection_failed_after_3_retries</c> is not a state.
  /// </summary>
  public static bool IsAllowed(string? state) => state is not null && AllSet.Contains(state);
}
