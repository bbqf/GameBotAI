using System.Collections.ObjectModel;

namespace GameBot.Domain.Actions;

public static class PrimitiveActionTypes {
  public const string Tap = "tap";
  public const string Swipe = "swipe";
  public const string Key = "key";
  public const string Command = "command";
  public const string ConnectToGame = "connect-to-game";
  public const string WaitForImage = "WaitForImage";
  public const string EnsureGameRunning = "ensure-game-running";

  public static IReadOnlyCollection<string> All { get; } = new ReadOnlyCollection<string>(new[] {
    Tap,
    Swipe,
    Key,
    Command,
    ConnectToGame,
    WaitForImage,
    EnsureGameRunning
  });
}

public abstract class PrimitiveActionBase {
  public string Type { get; set; } = string.Empty;
  public string? SchemaVersion { get; set; }

  protected PrimitiveActionBase() { }

  protected PrimitiveActionBase(string type) {
    Type = type;
  }
}

public enum PrimitiveActionSelectionContext {
  CommandStep,
  SequenceStep,
  ExecutionConnect
}

public sealed class PrimitiveActionSelection {
  public required PrimitiveActionBase PrimitiveAction { get; init; }
  public required PrimitiveActionSelectionContext Context { get; init; }
  public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
