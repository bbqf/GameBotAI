namespace GameBot.Domain.Actions;

/// <summary>
/// Canonical action type keys shared across authoring, persistence, and execution.
/// </summary>
public static class ActionTypes {
  public const string Command = "command";
  public const string Tap = "tap";
  public const string Swipe = "swipe";
  public const string Key = "key";
  public const string ConnectToGame = "connect-to-game";
  public const string WaitForImage = "WaitForImage";
  public const string EnsureGameRunning = "ensure-game-running";

  /// <summary>
  /// Self-reschedule action (feature 065): schedules one additional firing of the current
  /// sequence into its originating queue run. No-op success when not started from a queue.
  /// </summary>
  public const string RescheduleSelf = "reschedule-self";
}
