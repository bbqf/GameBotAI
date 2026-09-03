using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBot.Service.Models;

internal sealed record SequenceExecuteRequest {
  public string? SessionId { get; init; }
}

internal sealed record DelayRangeMsContract {
  public int Min { get; init; }
  public int Max { get; init; }
}

internal sealed record SequenceUpsertContract {
  public required string Name { get; init; }
  public int Version { get; init; }
  public required IReadOnlyList<SequenceStepContract> Steps { get; init; }
  public DelayRangeMsContract? InterStepDelayRangeMs { get; init; }

  /// <summary>Per-firing watchdog bound for queue runs, in ms; absent means the queue's default.</summary>
  public int? WatchdogTimeoutMs { get; init; }

  /// <summary>Parameters this sequence accepts (feature 078); absent means unparametrized.</summary>
  public IReadOnlyList<ParameterDeclarationDto>? Parameters { get; init; }
}

internal sealed record SequencePatchContract {
  public string? Name { get; init; }
  public int? Version { get; init; }
  public IReadOnlyList<SequenceStepContract>? Steps { get; init; }
  public DelayRangeMsContract? InterStepDelayRangeMs { get; init; }

  /// <summary>Per-firing watchdog bound for queue runs, in ms; absent leaves it unchanged.</summary>
  public int? WatchdogTimeoutMs { get; init; }

  /// <summary>Parameters this sequence accepts (feature 078); absent leaves them unchanged.</summary>
  public IReadOnlyList<ParameterDeclarationDto>? Parameters { get; init; }
}

internal sealed record SequenceStepContract {
  public required string StepId { get; init; }
  public string? Label { get; init; }
  public string? StepType { get; init; }
  public PrimitiveActionRequest? PrimitiveAction { get; init; }
  public SequenceCommandReferenceContract? CommandReference { get; init; }
  public SequenceStepConditionContract? Condition { get; init; }
  public LoopConfigContract? Loop { get; init; }
  public IReadOnlyList<SequenceStepContract>? Body { get; init; }
  public SequenceStepConditionContract? BreakCondition { get; init; }
  public IfConfigContract? If { get; init; }
  public IReadOnlyList<SequenceStepContract>? ElseBody { get; init; }

  /// <summary>
  /// Values bound for the invoked command's parameters (feature 078). Only meaningful on a command
  /// step; a binding with a null value means "inherit", which is the default for every row.
  /// </summary>
  public IReadOnlyList<ParameterBindingDto>? ParameterBindings { get; init; }

  /// <summary>
  /// Opt-in strictness: when <c>true</c>, this step fails (aborting the sequence) if the command it
  /// invokes puts no input on the device — typically an image-anchored tap that never found its
  /// template. Defaults to <c>false</c>, matching the long-standing "carry on" behavior that loops
  /// draining a list, and retry loops, depend on. Omitted from responses when false.
  /// </summary>
  public bool? RequireDispatch { get; init; }
}

/// <summary>
/// Optional body for <c>POST /api/sequences/{id}/execute</c> (feature 078, FR-031): values supplied
/// for an ad-hoc run that has no queue to inherit from.
/// </summary>
internal sealed record SequenceExecuteContract {
  public string? SessionId { get; init; }
  public IReadOnlyList<ParameterBindingDto>? Parameters { get; init; }
}

internal sealed record IfConfigContract {
  public required SequenceStepConditionContract Condition { get; init; }
}

internal sealed record SequenceCommandReferenceContract {
  public required string CommandId { get; init; }
  public string? CommandName { get; init; }
  public bool? IsResolved { get; init; }
}

internal sealed record WaitForImagePayloadContract {
  public DetectionTargetDto? DetectionTarget { get; init; }
  public int? TimeoutMs { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ImageVisibleConditionContract), typeDiscriminator: "imageVisible")]
[JsonDerivedType(typeof(CommandOutcomeConditionContract), typeDiscriminator: "commandOutcome")]
internal abstract record SequenceStepConditionContract {
  public bool Negate { get; init; }
}

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

  /// <summary>
  /// When true, running out of iterations ends the loop as an ordinary exit instead of failing
  /// the step and aborting the sequence. Omitted from responses when false.
  /// </summary>
  public bool? ExitOnMaxIterations { get; init; }
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

/// <summary>One row of the sequence list.</summary>
internal sealed record SequenceListItemResponse {
  public required string Id { get; init; }
  public required string Name { get; init; }
  public required System.Collections.ObjectModel.Collection<string> Steps { get; init; }

  /// <summary>
  /// Parameters this sequence declares (feature 078). Carried in the list because the queue-template
  /// editor renders a binding form per entry and would otherwise have to refetch every sequence.
  /// Omitted entirely when the sequence is unparametrized, so a pre-feature client sees exactly the
  /// payload it always saw.
  /// </summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public System.Collections.ObjectModel.Collection<ParameterDeclarationDto>? Parameters { get; init; }
}
