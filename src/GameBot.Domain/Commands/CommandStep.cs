namespace GameBot.Domain.Commands;

public enum CommandStepType {
  Command,
  PrimitiveTap,
  WaitForImage,
  EnsureGameRunning,
  KeyInput,
  Swipe,
  GoToHomeScreen,
  EnsureEmulatorRunning
}

/// <summary>
/// Config for the ensure-emulator-running command step (feature 070): identifies the LDPlayer
/// instance (by name or index) and the device serial used for the responsiveness probe.
/// </summary>
public sealed class EnsureEmulatorRunningConfig {
  public string? InstanceName { get; init; }
  public int? InstanceIndex { get; init; }
  public required string AdbSerial { get; init; }
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
  public EnsureEmulatorRunningConfig? EnsureEmulatorRunning { get; init; }
  public int Order { get; init; }
}
