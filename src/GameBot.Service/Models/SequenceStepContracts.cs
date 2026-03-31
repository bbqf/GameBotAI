using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBot.Service.Models;

internal sealed record SequenceExecuteRequest {
  public string? SessionId { get; init; }
}

internal sealed record SequenceUpsertContract {
  public required string Name { get; init; }
  public int Version { get; init; }
  public required IReadOnlyList<SequenceStepContract> Steps { get; init; }
}

internal sealed record SequenceStepContract {
  public required string StepId { get; init; }
  public string? Label { get; init; }
  public string? StepType { get; init; }
  public SequenceActionContract? Action { get; init; }
  public SequenceStepConditionContract? Condition { get; init; }
  public LoopConfigContract? Loop { get; init; }
  public IReadOnlyList<SequenceStepContract>? Body { get; init; }
  public SequenceStepConditionContract? BreakCondition { get; init; }
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "loopType")]
[JsonDerivedType(typeof(CountLoopConfigContract), typeDiscriminator: "count")]
[JsonDerivedType(typeof(WhileLoopConfigContract), typeDiscriminator: "while")]
[JsonDerivedType(typeof(RepeatUntilLoopConfigContract), typeDiscriminator: "repeatUntil")]
internal abstract record LoopConfigContract {
  public int? MaxIterations { get; init; }
}

internal sealed record CountLoopConfigContract : LoopConfigContract {
  public int Count { get; init; }
}

internal sealed record WhileLoopConfigContract : LoopConfigContract {
  public required SequenceStepConditionContract Condition { get; init; }
}

internal sealed record RepeatUntilLoopConfigContract : LoopConfigContract {
  public required SequenceStepConditionContract Condition { get; init; }
}
