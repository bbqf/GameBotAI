namespace GameBot.Domain.WaitForStill;

/// <summary>The outcome names of the WaitForStill step (feature 174).</summary>
public static class WaitForStillOutcomes {
  /// <summary>The watched part of the screen stayed the same for the stable time.</summary>
  public const string Still = "still";

  /// <summary>The time limit passed, or no frame could be read.</summary>
  public const string TimeoutElapsed = "timeout_elapsed";

  /// <summary>True when <paramref name="outcome"/> is an outcome name of a WaitForStill step.</summary>
  public static bool IsWaitForStillOutcome(string? outcome) =>
    string.Equals(outcome, Still, StringComparison.OrdinalIgnoreCase)
    || string.Equals(outcome, TimeoutElapsed, StringComparison.OrdinalIgnoreCase);
}
