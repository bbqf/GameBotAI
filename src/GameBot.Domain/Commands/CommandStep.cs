namespace GameBot.Domain.Commands;

public enum CommandStepType {
  Action,
  Command
}

public sealed class CommandStep {
  public required CommandStepType Type { get; init; }
  public required string TargetId { get; init; }
  public int Order { get; init; }
}
