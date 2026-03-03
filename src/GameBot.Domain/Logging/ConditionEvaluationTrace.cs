namespace GameBot.Domain.Logging;

public sealed record ConditionEvaluationTrace(
  bool FinalResult,
  string SelectedBranch,
  string? FailureReason,
  IReadOnlyList<Dictionary<string, object?>> OperandResults,
  IReadOnlyList<Dictionary<string, object?>> OperatorSteps);
