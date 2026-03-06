namespace GameBot.Service.Models;

internal abstract record SequenceStepContract {
  public required string StepType { get; init; }
  public string? Label { get; init; }
}

internal sealed record ActionStepContract : SequenceStepContract {
  public required string PayloadRef { get; init; }
}

internal sealed record ConditionalStepContract : SequenceStepContract {
  public required ImageVisibleConditionContract Condition { get; init; }
  public required string PayloadRef { get; init; }
}

internal sealed record ImageVisibleConditionContract {
  public required string Type { get; init; }
  public required string ImageId { get; init; }
  public double? MinSimilarity { get; init; }
}
