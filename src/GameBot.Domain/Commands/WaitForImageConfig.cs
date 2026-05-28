namespace GameBot.Domain.Commands;

public sealed class WaitForImageConfig {
  public DetectionTarget? DetectionTarget { get; init; }
  public int TimeoutMs { get; init; } = 1000;
}