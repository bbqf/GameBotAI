namespace GameBot.Service.Models;

internal sealed class ExecutionLogQueryDto
{
  public DateTimeOffset? FromUtc { get; init; }
  public DateTimeOffset? ToUtc { get; init; }
  public string? FinalStatus { get; init; }
  public string? ObjectType { get; init; }
  public string? ObjectId { get; init; }
  public int? PageSize { get; init; }
  public string? Cursor { get; init; }
}

internal sealed class ExecutionObjectReferenceDto
{
  public required string ObjectType { get; init; }
  public required string ObjectId { get; init; }
  public required string DisplayNameSnapshot { get; init; }
  public string? VersionSnapshot { get; init; }
}

internal sealed class ExecutionNavigationContextDto
{
  public required string DirectPath { get; init; }
  public string? ParentPath { get; init; }
  public required string PathKind { get; init; }
}

internal sealed class ExecutionHierarchyContextDto
{
  public required string RootExecutionId { get; init; }
  public string? ParentExecutionId { get; init; }
  public int Depth { get; init; }
  public int? SequenceIndex { get; init; }
}

internal sealed class ExecutionStepOutcomeDto
{
  public int StepOrder { get; init; }
  public required string StepType { get; init; }
  public required string Outcome { get; init; }
  public string? ReasonCode { get; init; }
  public string? ReasonText { get; init; }
}

internal sealed class ExecutionDetailItemDto
{
  public required string Kind { get; init; }
  public required string Message { get; init; }
  public Dictionary<string, object?>? Attributes { get; init; }
  public required string Sensitivity { get; init; }
}

internal sealed class ExecutionLogEntryDto
{
  public required string Id { get; init; }
  public DateTimeOffset TimestampUtc { get; init; }
  public required string ExecutionType { get; init; }
  public required string FinalStatus { get; init; }
  public required ExecutionObjectReferenceDto ObjectRef { get; init; }
  public required ExecutionNavigationContextDto Navigation { get; init; }
  public required ExecutionHierarchyContextDto Hierarchy { get; init; }
  public required string Summary { get; init; }
  public required IReadOnlyList<ExecutionDetailItemDto> Details { get; init; }
  public required IReadOnlyList<ExecutionStepOutcomeDto> StepOutcomes { get; init; }
}

internal sealed class ExecutionLogListResponseDto
{
  public required IReadOnlyList<ExecutionLogEntryDto> Items { get; init; }
  public string? NextPageToken { get; init; }
  public string? NextCursor { get; init; }
}

internal sealed class ExecutionLogListItemDto
{
  public required string Id { get; init; }
  public DateTimeOffset TimestampUtc { get; init; }
  public required string ObjectName { get; init; }
  public required string Status { get; init; }
  public bool HasSnapshot { get; init; }
}

internal sealed class RelatedObjectLinkDto
{
  public required string Label { get; init; }
  public required string TargetType { get; init; }
  public required string TargetId { get; init; }
  public bool IsAvailable { get; init; }
  public string? UnavailableReason { get; init; }
}

internal sealed class SnapshotReferenceDto
{
  public bool IsAvailable { get; init; }
  public string? ImageUrl { get; init; }
  public string? Caption { get; init; }
}

internal sealed class StepOutcomeDetailDto
{
  public required string StepName { get; init; }
  public required string Status { get; init; }
  public required string Message { get; init; }
  public DateTimeOffset? StartedAtUtc { get; init; }
  public DateTimeOffset? EndedAtUtc { get; init; }
}

internal sealed class ExecutionLogDetailDto
{
  public required string ExecutionId { get; init; }
  public required string Summary { get; init; }
  public required IReadOnlyList<RelatedObjectLinkDto> RelatedObjects { get; init; }
  public required SnapshotReferenceDto Snapshot { get; init; }
  public required IReadOnlyList<StepOutcomeDetailDto> StepOutcomes { get; init; }
}

internal sealed class ExecutionLogRetentionPolicyDto
{
  public bool Enabled { get; init; }
  public int RetentionDays { get; init; }
  public int CleanupIntervalMinutes { get; init; }
  public DateTimeOffset UpdatedAtUtc { get; init; }
}

internal sealed class ExecutionLogRetentionPolicyPatchDto
{
  public bool Enabled { get; init; }
  public int? RetentionDays { get; init; }
  public int? CleanupIntervalMinutes { get; init; }
}
