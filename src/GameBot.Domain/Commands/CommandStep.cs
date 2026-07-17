namespace GameBot.Domain.Commands;

public enum CommandStepType {
  Command,
  PrimitiveTap,
  WaitForImage,
  EnsureGameRunning,
  KeyInput,
  Swipe,
  GoToHomeScreen
}

public sealed class PrimitiveTapConfig {
  public required DetectionTarget DetectionTarget { get; init; }
}

public sealed class KeyInputConfig {
  public required string Key { get; init; }
}

public sealed class SwipeConfig {
  public required int StartX { get; init; }
  public required int StartY { get; init; }
  public required int EndX { get; init; }
  public required int EndY { get; init; }
  public int? DurationMs { get; init; }
}

public sealed class CommandStep {
  public required CommandStepType Type { get; init; }
  public string TargetId { get; init; } = string.Empty;
  public PrimitiveTapConfig? PrimitiveTap { get; init; }
  public WaitForImageConfig? WaitForImage { get; init; }
  public KeyInputConfig? KeyInput { get; init; }
  public SwipeConfig? Swipe { get; init; }
  public int Order { get; init; }
}
