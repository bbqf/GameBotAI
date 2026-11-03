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

  /// <summary>
  /// Feature 175: <c>present</c> ends the wait when the image shows. <c>absent</c> ends the wait when the
  /// image is gone. Null means <c>present</c>. Null is not written to a stored sequence, so a step without
  /// this option keeps its stored form. See <see cref="Execution.WaitForImageUntil"/>.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public string? Until { get; init; }

  /// <summary>
  /// Feature 175: the number of consecutive captures without the image that end an <c>absent</c> wait.
  /// Null means <see cref="Execution.WaitForImageUntil.DefaultAbsentCaptures"/>. It has no effect in
  /// <c>present</c> mode.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public int? AbsentCaptures { get; init; }
}
