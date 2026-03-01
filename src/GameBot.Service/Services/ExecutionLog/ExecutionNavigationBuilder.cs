using GameBot.Domain.Logging;

namespace GameBot.Service.Services.ExecutionLog;

internal static class ExecutionNavigationBuilder
{
  public static ExecutionNavigationContext Build(string objectType, string objectId, ExecutionLogContext? context)
  {
    var directPath = BuildPath(objectType, objectId) ?? "/authoring/unknown";
    var parentPath = BuildPath(context?.ParentObjectType, context?.ParentObjectId);
    return new ExecutionNavigationContext(directPath, parentPath);
  }

  private static string? BuildPath(string? objectType, string? objectId)
  {
    if (string.IsNullOrWhiteSpace(objectType) || string.IsNullOrWhiteSpace(objectId))
    {
      return null;
    }

    return objectType.Trim().ToLowerInvariant() switch
    {
      "command" => $"/authoring/commands/{objectId}",
      "sequence" => $"/authoring/sequences/{objectId}",
      _ => $"/authoring/{objectType.ToLowerInvariant()}/{objectId}"
    };
  }
}
