namespace GameBot.Service.Services.ExecutionLog;

internal sealed class ExecutionLogContext
{
  public string? ParentExecutionId { get; init; }
  public string? RootExecutionId { get; init; }
  public int Depth { get; init; }
  public int? SequenceIndex { get; init; }
  public string? ParentObjectType { get; init; }
  public string? ParentObjectId { get; init; }
}
