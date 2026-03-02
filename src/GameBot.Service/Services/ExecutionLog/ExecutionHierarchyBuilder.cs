using GameBot.Domain.Logging;

namespace GameBot.Service.Services.ExecutionLog;

internal static class ExecutionHierarchyBuilder {
  public static ExecutionHierarchyContext Build(ExecutionLogContext? context) {
    var parentExecutionId = NullIfWhiteSpace(context?.ParentExecutionId);
    var rootExecutionId = NullIfWhiteSpace(context?.RootExecutionId)
                          ?? parentExecutionId
                          ?? Guid.NewGuid().ToString("N");

    return new ExecutionHierarchyContext(
      rootExecutionId,
      parentExecutionId,
      Math.Max(0, context?.Depth ?? 0),
      context?.SequenceIndex);
  }

  private static string? NullIfWhiteSpace(string? value)
    => string.IsNullOrWhiteSpace(value) ? null : value;
}
