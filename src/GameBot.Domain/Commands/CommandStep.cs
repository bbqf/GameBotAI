namespace GameBot.Domain.Commands;

public enum CommandStepType {
  Action,
  Command,
  PrimitiveTap
}

public sealed class PrimitiveTapConfig {
  public required DetectionTarget DetectionTarget { get; init; }
}

public sealed class CommandStep {
  public required CommandStepType Type { get; init; }
  public required string TargetId { get; init; }
  public PrimitiveTapConfig? PrimitiveTap { get; init; }
  public int Order { get; init; }
}
