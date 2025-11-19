namespace GameBot.Domain.Actions;

public sealed class InputAction {
  public required string Type { get; init; }
  public Dictionary<string, object> Args { get; init; } = new();
  public int? DelayMs { get; init; }
  public int? DurationMs { get; init; }
}
