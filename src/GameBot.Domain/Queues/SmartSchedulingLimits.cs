namespace GameBot.Domain.Queues {
  /// <summary>
  /// The limits of the smart scheduling window of a queue (feature 189).
  /// </summary>
  public static class SmartSchedulingLimits {
    /// <summary>The window of a new queue and of a queue file from before feature 189, in minutes.</summary>
    public const int DefaultWindowMinutes = 15;

    /// <summary>The smallest permitted window, in minutes.</summary>
    public const int MinWindowMinutes = 1;

    /// <summary>The largest permitted window, in minutes.</summary>
    public const int MaxWindowMinutes = 120;

    /// <summary>
    /// Returns true when <paramref name="minutes"/> is from <see cref="MinWindowMinutes"/> to
    /// <see cref="MaxWindowMinutes"/>, the two ends included.
    /// </summary>
    /// <param name="minutes">The window to check, in minutes.</param>
    public static bool IsValidWindow(int minutes) =>
      minutes >= MinWindowMinutes && minutes <= MaxWindowMinutes;
  }
}
