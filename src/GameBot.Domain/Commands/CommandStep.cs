namespace GameBot.Domain.Commands;

public enum CommandStepType {
  Command,
  PrimitiveTap,
  WaitForImage,
  EnsureGameRunning
}

public sealed class PrimitiveTapConfig {
  public required DetectionTarget DetectionTarget { get; init; }
}

public sealed class CommandStep {
  public required CommandStepType Type { get; init; }
  public string TargetId { get; init; } = string.Empty;
  public PrimitiveTapConfig? PrimitiveTap { get; init; }
  public WaitForImageConfig? WaitForImage { get; init; }
  public int Order { get; init; }
}
