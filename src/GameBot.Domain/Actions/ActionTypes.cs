namespace GameBot.Domain.Actions;

/// <summary>
/// Canonical action type keys shared across authoring, persistence, and execution.
/// </summary>
public static class ActionTypes {
  public const string Tap = "tap";
  public const string Swipe = "swipe";
  public const string Key = "key";
  public const string ConnectToGame = "connect-to-game";
}