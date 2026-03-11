using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBot.Service.Models;

internal sealed record SequenceUpsertContract {
  public required string Name { get; init; }
  public int Version { get; init; }
  public required IReadOnlyList<SequenceStepContract> Steps { get; init; }
}

internal sealed record SequenceStepContract {
  public required string StepId { get; init; }
  public string? Label { get; init; }
  public required SequenceActionContract Action { get; init; }
  public SequenceStepConditionContract? Condition { get; init; }
}

internal sealed record SequenceActionContract {
  public required string Type { get; init; }
  public Dictionary<string, JsonElement> Parameters { get; init; } = new(StringComparer.Ordinal);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ImageVisibleConditionContract), typeDiscriminator: "imageVisible")]
[JsonDerivedType(typeof(CommandOutcomeConditionContract), typeDiscriminator: "commandOutcome")]
internal abstract record SequenceStepConditionContract;

internal sealed record ImageVisibleConditionContract : SequenceStepConditionContract {
  public required string ImageId { get; init; }
  public double? MinSimilarity { get; init; }
}

internal sealed record CommandOutcomeConditionContract : SequenceStepConditionContract {
  public required string StepRef { get; init; }
  public required string ExpectedState { get; init; }
}
