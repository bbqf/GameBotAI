namespace GameBot.Domain.Services;

/// <summary>
/// The outcome states that a <c>commandOutcome</c> condition may name (feature 129 added
/// <c>restarted</c>). One list serves the validators, the evaluator, and the API text.
/// </summary>
public static class StepOutcomeStates {
  /// <summary>The step ran and did its work.</summary>
  public const string Success = "success";

  /// <summary>The step failed.</summary>
  public const string Failed = "failed";

  /// <summary>The step did not run.</summary>
  public const string Skipped = "skipped";

  /// <summary>The step ended a loop with a break.</summary>
  public const string Break = "break";

  /// <summary>The step did not break.</summary>
  public const string NoBreak = "no_break";

  /// <summary>An ensure-game-running step stopped and started the game (feature 129).</summary>
  public const string Restarted = "restarted";

  /// <summary>The allowed states, in the order that the error text shows them.</summary>
  public static IReadOnlyList<string> All { get; } =
    new[] { Success, Failed, Skipped, Break, NoBreak, Restarted };

  /// <summary>The allowed states as one text, for example <c>success|failed|...</c>.</summary>
  public static string AllText { get; } = string.Join('|', All);

  private static readonly HashSet<string> AllSet = new(All, StringComparer.OrdinalIgnoreCase);

  /// <summary>True when <paramref name="state"/> is an allowed state.</summary>
  public static bool IsAllowed(string? state) => state is not null && AllSet.Contains(state);

  /// <summary>
  /// True when a step whose recorded state is <paramref name="actual"/> satisfies a condition that
  /// expects <paramref name="expected"/>. A restarted step also matches <c>success</c>, so an existing
  /// condition on <c>success</c> keeps working. <c>restarted</c> matches only a restarted step.
  /// </summary>
  public static bool Matches(string? actual, string? expected) {
    if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
    return string.Equals(actual, Restarted, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expected, Success, StringComparison.OrdinalIgnoreCase);
  }
}
