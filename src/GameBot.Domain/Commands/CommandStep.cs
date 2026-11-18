using System.Collections.Generic;
using System.Collections.ObjectModel;
using GameBot.Domain.Parameters;

namespace GameBot.Domain.Commands;

public enum CommandStepType {
  Command,
  PrimitiveTap,
  WaitForImage,
  EnsureGameRunning,
  KeyInput,
  Swipe,
  GoToHomeScreen,
  EnsureEmulatorRunning,
  TextInput
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

/// <summary>
/// Config for the ensure-game-running command step. When <see cref="ReadinessImage"/> is set, the
/// step does not report success as soon as the game package is foreground; instead, after launching
/// the game it polls the screen for the readiness image (e.g. the city-view sentinel) for up to
/// <see cref="ReadinessTimeoutMs"/> milliseconds, so a cold-launched game that is still on its
/// splash/loading screen does not let the queue proceed prematurely. When null the step keeps its
/// legacy behavior (success iff the game is already foreground).
/// </summary>
public sealed class EnsureGameRunningConfig {
  public DetectionTarget? ReadinessImage { get; init; }
  public int ReadinessTimeoutMs { get; init; } = 90_000;

  /// <summary>
  /// When true (feature 129), the step stops the game, starts it again, and waits for the foreground
  /// instead of the plain check. False is the default and is not written to storage.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(
      Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
  public bool ForceRestart { get; init; }
}

public sealed class PrimitiveTapConfig {
  /// <summary>The smallest permitted <see cref="HoldMs"/> (feature 111).</summary>
  public const int MinHoldMs = 0;

  /// <summary>The largest permitted <see cref="HoldMs"/> (feature 111).</summary>
  public const int MaxHoldMs = 5000;

  /// <summary>The duration in milliseconds of a single tap. The tap is a swipe to the same point.</summary>
  public const int DefaultTapDurationMs = 200;

  public required DetectionTarget DetectionTarget { get; init; }

  /// <summary>
  /// Optional hold duration in milliseconds (feature 111), from <see cref="MinHoldMs"/> to
  /// <see cref="MaxHoldMs"/>. Absent or 0 gives a single tap. More than 0 gives a press and hold
  /// at the detected point (plus the offsets) for this duration. Null is not written to storage.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(
      Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public int? HoldMs { get; init; }
}

public sealed class KeyInputConfig {
  public required string Key { get; init; }
}

/// <summary>
/// Config for the text input command step (feature 192). The service types <see cref="Text"/> into
/// the input field that has the focus on the device.
/// </summary>
public sealed class TextInputConfig {
  /// <summary>
  /// The text to type. It can hold inline placeholders <c>{{name}}</c>. A literal text must pass
  /// <see cref="TextInputRules.Check"/>. The text can be empty when the step has the
  /// <c>textInput.text</c> key in <see cref="CommandStep.FieldTemplates"/>.
  /// </summary>
  public required string Text { get; init; }
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
  public EnsureGameRunningConfig? EnsureGameRunning { get; init; }

  /// <summary>
  /// The config of a <see cref="CommandStepType.TextInput"/> step (feature 192). Null is not written
  /// to storage, so a stored command without a text input step does not change.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(
      Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public TextInputConfig? TextInput { get; init; }

  public int Order { get; init; }

  /// <summary>
  /// Parameter placeholders for configuration fields of this step (features 078 and 114), keyed
  /// by dotted field path — for example <c>{"swipe.startX": "{{originX}}"}</c>.
  /// <para>
  /// The numeric keys exist because a placeholder cannot be stored in an <c>int</c>. The two image
  /// keys (<c>primitiveTap.detectionTarget.referenceImageId</c> and
  /// <c>waitForImage.detectionTarget.referenceImageId</c>) let a parameter replace the inline image
  /// id at dispatch. The text key <c>textInput.text</c> (feature 192) lets a parameter replace the
  /// inline text of a text input step. The value of an image key or a text key must be one whole
  /// placeholder. Other text fields keep their placeholder inline. See <see cref="CommandStepFieldPaths"/> for the supported keys.
  /// The save rejects an unsupported key, so a typo cannot silently do nothing.
  /// </para>
  /// <para><c>null</c> or empty means no field is parametrized through this overlay.</para>
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(
      Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, string>? FieldTemplates { get; init; }

  /// <summary>
  /// Values bound for the parameters of the command this step invokes (feature 078). Meaningful only
  /// when <see cref="Type"/> is <see cref="CommandStepType.Command"/>. A binding whose value is null
  /// means "inherit", which is the default for every row, so the common case needs no configuration.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(
      Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public Collection<ParameterBinding>? ParameterBindings { get; init; }
}

/// <summary>
/// The complete set of dotted field paths accepted by <see cref="CommandStep.FieldTemplates"/>
/// (features 078, 114, and 192): the numeric paths, with the numeric kind of each target, the image
/// paths, and the text paths.
/// </summary>
public static class CommandStepFieldPaths {
  /// <summary>Supported path → true when the target field is an integer, false when it is a double.</summary>
  public static IReadOnlyDictionary<string, bool> SupportedNumericPaths { get; } =
      new Dictionary<string, bool>(System.StringComparer.Ordinal) {
        ["swipe.startX"] = true,
        ["swipe.startY"] = true,
        ["swipe.endX"] = true,
        ["swipe.endY"] = true,
        ["swipe.durationMs"] = true,
        ["waitForImage.timeoutMs"] = true,
        ["ensureEmulatorRunning.instanceIndex"] = true,
        ["ensureGameRunning.readinessTimeoutMs"] = true,
        ["primitiveTap.detectionTarget.confidence"] = false,
        ["primitiveTap.detectionTarget.offsetX"] = true,
        ["primitiveTap.detectionTarget.offsetY"] = true,
        ["ensureGameRunning.readinessImage.confidence"] = false,
        ["ensureGameRunning.readinessImage.offsetX"] = true,
        ["ensureGameRunning.readinessImage.offsetY"] = true
      };

  /// <summary>
  /// The image paths (feature 114). The value of an image path must be one whole placeholder. At
  /// dispatch, the resolved value replaces the inline image id.
  /// </summary>
  public static IReadOnlySet<string> SupportedImagePaths { get; } =
      new HashSet<string>(System.StringComparer.Ordinal) {
        "primitiveTap.detectionTarget.referenceImageId",
        "waitForImage.detectionTarget.referenceImageId"
      };

  /// <summary>
  /// The text paths (feature 192). The value of a text path must be one whole placeholder. At
  /// dispatch, the resolved value replaces the inline text.
  /// </summary>
  public static IReadOnlySet<string> SupportedTextPaths { get; } =
      new HashSet<string>(System.StringComparer.Ordinal) {
        "textInput.text"
      };

  /// <summary>True when <paramref name="path"/> is a supported numeric key, image key, or text key.</summary>
  /// <param name="path">Dotted field path to check.</param>
  public static bool IsSupported(string? path) =>
      path is not null && (SupportedNumericPaths.ContainsKey(path)
          || SupportedImagePaths.Contains(path)
          || SupportedTextPaths.Contains(path));

  /// <summary>True when <paramref name="path"/> is a supported image key.</summary>
  /// <param name="path">Dotted field path to check.</param>
  public static bool IsImagePath(string? path) =>
      path is not null && SupportedImagePaths.Contains(path);

  /// <summary>True when <paramref name="path"/> is a supported text key (feature 192).</summary>
  /// <param name="path">Dotted field path to check.</param>
  public static bool IsTextPath(string? path) =>
      path is not null && SupportedTextPaths.Contains(path);
}
