using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace GameBot.Service.Models;

internal sealed class CreateCommandRequest {
  public required string Name { get; set; }
  public string? TriggerId { get; set; }
  public Collection<CommandStepDto> Steps { get; init; } = new();
  public DetectionTargetDto? Detection { get; init; }
}

internal sealed class UpdateCommandRequest {
  public string? Name { get; set; }
  public string? TriggerId { get; set; }
  public Collection<CommandStepDto>? Steps { get; init; }

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
}

internal enum CommandStepTypeDto {
  Action,
  Command,
  PrimitiveTap
}

internal sealed class PrimitiveTapConfigDto {
  public required DetectionTargetDto DetectionTarget { get; init; }
}

internal sealed class CommandStepDto {
  public required CommandStepTypeDto Type { get; init; }
  public string? TargetId { get; init; }
  public PrimitiveTapConfigDto? PrimitiveTap { get; init; }
  public int Order { get; init; }
}

internal sealed class ResolvedPointDto {
  public required int X { get; init; }
  public required int Y { get; init; }
}

internal sealed class StepExecutionOutcomeDto {
  public required int StepOrder { get; init; }
  public required string Status { get; init; }
  public string? Reason { get; init; }
  public ResolvedPointDto? ResolvedPoint { get; init; }
  public double? DetectionConfidence { get; init; }
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
