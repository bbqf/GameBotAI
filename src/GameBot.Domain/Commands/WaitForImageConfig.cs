namespace GameBot.Domain.Commands;

/// <summary>
/// Configuration payload for a <c>WaitForImage</c> step.
/// </summary>
public sealed class WaitForImageConfig {
  /// <summary>
  /// Optional image detection target used to end the wait early when the image is detected.
  /// When omitted, the step behaves as a pure timeout wait.
  /// </summary>
  public DetectionTarget? DetectionTarget { get; init; }

  /// <summary>
  /// Maximum wait duration in milliseconds.
  /// </summary>
  public int TimeoutMs { get; init; } = 1000;
}
