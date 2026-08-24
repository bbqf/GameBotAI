using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace GameBot.Service.Models;

/// <summary>
/// A parameter a command or sequence declares (feature 078). Wire shape of
/// <see cref="GameBot.Domain.Parameters.ParameterDeclaration"/>.
/// </summary>
internal sealed class ParameterDeclarationDto {
  public string? Name { get; init; }
  /// <summary>"text" or "number"; defaults to "text" when omitted.</summary>
  public string? Type { get; init; }
  public string? Default { get; init; }
  public bool? Required { get; init; }
  public string? Description { get; init; }
}

/// <summary>
/// A value supplied for a parameter at one call site (feature 078). A null/absent
/// <see cref="Value"/> means "inherit from the enclosing scope".
/// </summary>
internal sealed class ParameterBindingDto {
  public string? Name { get; init; }
  public string? Value { get; init; }
}

/// <summary>
/// One name visible in a scope, with its effective value and the layer that supplied it (feature 078).
/// Read-only; feeds the insert-parameter picker and the effective-value preview.
/// </summary>
internal sealed class ParameterScopeEntryDto {
  public string? Name { get; init; }
  public string? Value { get; init; }
  public string? OriginLayer { get; init; }
  public bool Declared { get; init; }
  public string? Description { get; init; }
}

/// <summary>Non-blocking parameter advisory returned alongside a successful save (feature 078).</summary>
internal sealed class ParameterWarningDto {
  public string? Code { get; init; }
  public string? Message { get; init; }
  public string? FieldPath { get; init; }
  public string? ParameterName { get; init; }
  public int? EntryIndex { get; init; }
}

internal sealed class CreateCommandRequest {
  public required string Name { get; set; }
  public string? TriggerId { get; set; }
  public Collection<CommandStepDto> Steps { get; init; } = new();
  public DetectionTargetDto? Detection { get; init; }

  /// <summary>Parameters this command accepts (feature 078); absent means unparametrized.</summary>
  public Collection<ParameterDeclarationDto>? Parameters { get; init; }
}

internal sealed class UpdateCommandRequest {
  public string? Name { get; set; }
  public string? TriggerId { get; set; }
  public Collection<CommandStepDto>? Steps { get; init; }

  /// <summary>Parameters this command accepts (feature 078); absent leaves them unchanged.</summary>
  public Collection<ParameterDeclarationDto>? Parameters { get; init; }

  private DetectionTargetDto? _detection;

  public DetectionTargetDto? Detection {
    get => _detection;
    set {
      _detection = value;
      DetectionSpecified = true;
    }
  }

  [JsonIgnore]
  public bool DetectionSpecified { get; private set; }
}

internal sealed class CommandResponse {
  public required string Id { get; init; }
  public required string Name { get; init; }
  public string? TriggerId { get; init; }
  public Collection<CommandStepDto> Steps { get; init; } = new();
  public DetectionTargetDto? Detection { get; init; }

  /// <summary>
  /// Parameters this command declares (feature 078). Omitted from the response entirely when the
  /// command is unparametrized, so a pre-feature client sees exactly the payload it always saw.
  /// </summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Collection<ParameterDeclarationDto>? Parameters { get; init; }

  /// <summary>Non-blocking parameter advisories; omitted when there are none.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Collection<ParameterWarningDto>? Warnings { get; init; }
}

internal enum CommandStepTypeDto {
  Command,
  PrimitiveTap,
  WaitForImage,
  EnsureGameRunning,
  KeyInput,
  Swipe,
  GoToHomeScreen,
  EnsureEmulatorRunning
}

internal sealed class EnsureEmulatorRunningConfigDto {
  public string? InstanceName { get; init; }
  public int? InstanceIndex { get; init; }
  public required string AdbSerial { get; init; }
}

internal sealed class KeyInputConfigDto {
  public required string Key { get; init; }
}

internal sealed class SwipeConfigDto {
  public required int StartX { get; init; }
  public required int StartY { get; init; }
  public required int EndX { get; init; }
  public required int EndY { get; init; }
  public int? DurationMs { get; init; }
}

internal sealed class PrimitiveTapConfigDto {
  public required DetectionTargetDto DetectionTarget { get; init; }
}

internal sealed class WaitForImageConfigDto {
  public DetectionTargetDto? DetectionTarget { get; init; }
  public int? TimeoutMs { get; init; }
}

internal sealed class EnsureGameRunningConfigDto {
  public DetectionTargetDto? ReadinessImage { get; init; }
  public int? ReadinessTimeoutMs { get; init; }
}

internal sealed class CommandStepDto {
  public required CommandStepTypeDto Type { get; init; }
  public string? TargetId { get; init; }
  public PrimitiveTapConfigDto? PrimitiveTap { get; init; }
  public WaitForImageConfigDto? WaitForImage { get; init; }
  public KeyInputConfigDto? KeyInput { get; init; }
  public SwipeConfigDto? Swipe { get; init; }
  public EnsureEmulatorRunningConfigDto? EnsureEmulatorRunning { get; init; }
  public EnsureGameRunningConfigDto? EnsureGameRunning { get; init; }
  public int Order { get; init; }

  /// <summary>
  /// Placeholders for this step's numeric fields, keyed by dotted path (feature 078). String fields
  /// carry their placeholder inline instead and never appear here.
  /// </summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, string>? FieldTemplates { get; init; }

  /// <summary>Values bound for the invoked command's parameters; only meaningful for Command steps.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Collection<ParameterBindingDto>? ParameterBindings { get; init; }
}

internal sealed class ResolvedPointDto {
  public required int X { get; init; }
  public required int Y { get; init; }
}

internal sealed class SwipePointsDto {
  public required ResolvedPointDto Start { get; init; }
  public required ResolvedPointDto End { get; init; }
}

internal sealed class StepExecutionOutcomeDto {
  public required int StepOrder { get; init; }
  public required string Status { get; init; }
  public string? StepType { get; init; }
  public string? Reason { get; init; }
  public ResolvedPointDto? ResolvedPoint { get; init; }
  public double? DetectionConfidence { get; init; }
  public int? TimeoutMs { get; init; }
  public int? EffectiveTimeoutMs { get; init; }
  public string? ReferenceImageId { get; init; }
  public string? ImageLoadStatus { get; init; }
  public ResolvedPointDto? ExecutedPoint { get; init; }
  public SwipePointsDto? TargetSwipe { get; init; }
  public SwipePointsDto? ExecutedSwipe { get; init; }
}

internal enum DetectionSelectionStrategyDto {
  HighestConfidence,
  FirstMatch
}

internal sealed class DetectionTargetDto {
  public required string ReferenceImageId { get; init; }
  public double? Confidence { get; init; }
  public int? OffsetX { get; init; }
  public int? OffsetY { get; init; }
  public DetectionSelectionStrategyDto? SelectionStrategy { get; init; }
}
