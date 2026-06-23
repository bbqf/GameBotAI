namespace GameBot.Service.Services.ExecutionLog;

internal sealed class ExecutionLogContext {
  public string? ParentExecutionId { get; init; }
  public string? RootExecutionId { get; init; }
  public int Depth { get; init; }
  public int? SequenceIndex { get; init; }
  public string? ParentObjectType { get; init; }
  public string? ParentObjectId { get; init; }
  public string? SequenceId { get; init; }
  public string? SequenceLabel { get; init; }
  public string? StepId { get; init; }
  public string? StepLabel { get; init; }

  /// <summary>
  /// The id of the queue run that launched this sequence firing, propagated through nesting
  /// (feature 065, FR-018). Non-empty ⇒ the sequence was "started from a queue" and a
  /// self-reschedule action may schedule into that run; null/empty ⇒ standalone run (no-op success).
  /// </summary>
  public string? OriginatingQueueId { get; init; }

  /// <summary>
  /// When set, this firing was produced by a self-reschedule action; carries the originating
  /// action's entry id for attribution in the execution log (feature 065, FR-014).
  /// </summary>
  public string? SelfRescheduleOriginActionId { get; init; }
}
