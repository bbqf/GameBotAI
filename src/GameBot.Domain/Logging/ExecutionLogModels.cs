namespace GameBot.Domain.Logging;

public sealed record ExecutionObjectReference(
  string ObjectType,
  string ObjectId,
  string DisplayNameSnapshot,
  string? VersionSnapshot = null);

public sealed record ExecutionNavigationContext(
  string DirectPath,
  string? ParentPath,
  string PathKind = "relative-route");

public sealed record ExecutionHierarchyContext(
  string RootExecutionId,
  string? ParentExecutionId,
  int Depth,
  int? SequenceIndex);

/// <summary>
/// Captures the outcome of a single iteration within a loop step execution.
/// </summary>
public sealed record LoopIterationOutcome(
  /// <summary>1-based iteration index.</summary>
  int IterationIndex,
  /// <summary>Whether a break step terminated this iteration early.</summary>
  bool BreakTriggered,
  /// <summary>Outcomes of the individual body steps run during this iteration.</summary>
  IReadOnlyList<string> StepOutcomes);

public sealed record ExecutionStepOutcome(
  int StepOrder,
  string StepType,
  string Outcome,
  string? ReasonCode,
  string? ReasonText,
  string? SequenceId = null,
  string? StepId = null,
  string? SequenceLabel = null,
  string? StepLabel = null,
  ConditionEvaluationTrace? ConditionTrace = null)
{
  /// <summary>
  /// Per-iteration outcomes recorded for loop steps.  <c>null</c> for non-loop steps.
  /// </summary>
  public IReadOnlyList<LoopIterationOutcome>? LoopIterations { get; init; }
}

public sealed record ExecutionDetailItem(
  string Kind,
  string Message,
  Dictionary<string, object?>? Attributes,
  string Sensitivity);

public sealed class ExecutionLogEntry
{
  public string Id { get; init; } = Guid.NewGuid().ToString("N");
  public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
  public string ExecutionType { get; init; } = "command";
  public string FinalStatus { get; init; } = "success";
  public required ExecutionObjectReference ObjectRef { get; init; }
  public required ExecutionNavigationContext Navigation { get; init; }
  public required ExecutionHierarchyContext Hierarchy { get; init; }
  public string Summary { get; init; } = string.Empty;
  public IReadOnlyList<ExecutionDetailItem> Details { get; init; } = Array.Empty<ExecutionDetailItem>();
  public IReadOnlyList<ExecutionStepOutcome> StepOutcomes { get; init; } = Array.Empty<ExecutionStepOutcome>();
  public DateTimeOffset RetentionExpiresUtc { get; init; }
}

public sealed class ExecutionLogQuery
{
  public string? SortBy { get; init; } = "timestamp";
  public string? SortDirection { get; init; } = "desc";
  public string? FilterTimestamp { get; init; }
  public string? FilterObjectName { get; init; }
  public string? FilterStatus { get; init; }
  public string? PageToken { get; init; }

  public DateTimeOffset? FromUtc { get; init; }
  public DateTimeOffset? ToUtc { get; init; }
  public string? FinalStatus { get; init; }
  public string? ObjectType { get; init; }
  public string? ObjectId { get; init; }
  public int PageSize { get; init; } = 50;
  public string? Cursor { get; init; }
}

public sealed record ExecutionLogPage(IReadOnlyList<ExecutionLogEntry> Items, string? NextCursor);
