namespace GameBot.Service.Contracts.Sequences;

internal sealed class SequenceFlowUpsertRequestDto {
  public string Name { get; set; } = string.Empty;
  public int Version { get; set; }
  public string EntryStepId { get; set; } = string.Empty;
  public IReadOnlyList<FlowStepDto> Steps { get; set; } = Array.Empty<FlowStepDto>();
  public IReadOnlyList<BranchLinkDto> Links { get; set; } = Array.Empty<BranchLinkDto>();
}

internal sealed class SequenceFlowDto {
  public string SequenceId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public int Version { get; set; }
  public string EntryStepId { get; set; } = string.Empty;
  public IReadOnlyList<FlowStepDto> Steps { get; set; } = Array.Empty<FlowStepDto>();
  public IReadOnlyList<BranchLinkDto> Links { get; set; } = Array.Empty<BranchLinkDto>();
}

internal sealed class FlowStepDto {
  public string StepId { get; set; } = string.Empty;
  public string Label { get; set; } = string.Empty;
  public string StepType { get; set; } = string.Empty;
  public string? PayloadRef { get; set; }
  public int? IterationLimit { get; set; }
  public ConditionExpressionDto? Condition { get; set; }
}

internal sealed class BranchLinkDto {
  public string LinkId { get; set; } = string.Empty;
  public string SourceStepId { get; set; } = string.Empty;
  public string TargetStepId { get; set; } = string.Empty;
  public string BranchType { get; set; } = string.Empty;
}

internal sealed class ConditionExpressionDto {
  public string NodeType { get; set; } = string.Empty;
  public IReadOnlyList<ConditionExpressionDto>? Children { get; set; }
  public ConditionOperandDto? Operand { get; set; }
}

internal sealed class ConditionOperandDto {
  public string OperandType { get; set; } = string.Empty;
  public string TargetRef { get; set; } = string.Empty;
  public string ExpectedState { get; set; } = string.Empty;
  public double? Threshold { get; set; }
}

internal sealed class SequenceSaveConflictDto {
  public string SequenceId { get; set; } = string.Empty;
  public int CurrentVersion { get; set; }
  public string Message { get; set; } = string.Empty;
}

internal sealed class AuthoringDeepLinkDto {
  public string SequenceId { get; set; } = string.Empty;
  public string StepId { get; set; } = string.Empty;
  public string SequenceLabel { get; set; } = string.Empty;
  public string StepLabel { get; set; } = string.Empty;
  public string ResolutionStatus { get; set; } = string.Empty;
  public string? FallbackRoute { get; set; }
}

internal sealed class ConditionEvaluationTraceDto {
  public bool FinalResult { get; set; }
  public string SelectedBranch { get; set; } = string.Empty;
  public string? FailureReason { get; set; }
  public IReadOnlyList<object> OperandResults { get; set; } = Array.Empty<object>();
  public IReadOnlyList<object> OperatorSteps { get; set; } = Array.Empty<object>();
}
