using GameBot.Domain.Logging;
using GameBot.Service.Models;
using GameBot.Service.Services.ExecutionLog;

namespace GameBot.Service.Endpoints;

internal static class ExecutionLogsEndpoints
{
  public static IEndpointRouteBuilder MapExecutionLogEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup(ApiRoutes.ExecutionLogs).WithTags("ExecutionLogs");

    group.MapGet("", async (
      DateTimeOffset? fromUtc,
      DateTimeOffset? toUtc,
      string? finalStatus,
      string? objectType,
      string? objectId,
      int? pageSize,
      string? cursor,
      IExecutionLogService svc,
      CancellationToken ct) =>
    {
      var page = await svc.QueryAsync(new ExecutionLogQuery
      {
        FromUtc = fromUtc,
        ToUtc = toUtc,
        FinalStatus = finalStatus,
        ObjectType = objectType,
        ObjectId = objectId,
        PageSize = pageSize ?? 50,
        Cursor = cursor
      }, ct).ConfigureAwait(false);

      return Results.Ok(new ExecutionLogListResponseDto
      {
        Items = page.Items.Select(ToDto).ToArray(),
        NextCursor = page.NextCursor
      });
    }).WithName("ListExecutionLogs");

    group.MapGet("/{id}", async (string id, IExecutionLogService svc, CancellationToken ct) =>
    {
      var item = await svc.GetAsync(id, ct).ConfigureAwait(false);
      if (item is null)
      {
        return Results.NotFound(new
        {
          error = new { code = "not_found", message = "Execution log entry not found", hint = (string?)null }
        });
      }

      return Results.Ok(ToDto(item));
    }).WithName("GetExecutionLog");

    group.MapGet("/retention", async (IExecutionLogService svc, CancellationToken ct) =>
    {
      var policy = await svc.GetRetentionAsync(ct).ConfigureAwait(false);
      return Results.Ok(new ExecutionLogRetentionPolicyDto
      {
        Enabled = policy.Enabled,
        RetentionDays = policy.RetentionDays,
        CleanupIntervalMinutes = policy.CleanupIntervalMinutes,
        UpdatedAtUtc = policy.UpdatedAtUtc
      });
    }).WithName("GetExecutionLogRetentionPolicy");

    group.MapPut("/retention", async (ExecutionLogRetentionPolicyPatchDto patch, IExecutionLogService svc, CancellationToken ct) =>
    {
      var updated = await svc.UpdateRetentionAsync(patch.Enabled, patch.RetentionDays, patch.CleanupIntervalMinutes, ct).ConfigureAwait(false);
      return Results.Ok(new ExecutionLogRetentionPolicyDto
      {
        Enabled = updated.Enabled,
        RetentionDays = updated.RetentionDays,
        CleanupIntervalMinutes = updated.CleanupIntervalMinutes,
        UpdatedAtUtc = updated.UpdatedAtUtc
      });
    }).WithName("UpdateExecutionLogRetentionPolicy");

    return app;
  }

  private static ExecutionLogEntryDto ToDto(ExecutionLogEntry entry)
    => new()
    {
      Id = entry.Id,
      TimestampUtc = entry.TimestampUtc,
      ExecutionType = entry.ExecutionType,
      FinalStatus = entry.FinalStatus,
      ObjectRef = new ExecutionObjectReferenceDto
      {
        ObjectType = entry.ObjectRef.ObjectType,
        ObjectId = entry.ObjectRef.ObjectId,
        DisplayNameSnapshot = entry.ObjectRef.DisplayNameSnapshot,
        VersionSnapshot = entry.ObjectRef.VersionSnapshot
      },
      Navigation = new ExecutionNavigationContextDto
      {
        DirectPath = entry.Navigation.DirectPath,
        ParentPath = entry.Navigation.ParentPath,
        PathKind = entry.Navigation.PathKind
      },
      Hierarchy = new ExecutionHierarchyContextDto
      {
        RootExecutionId = entry.Hierarchy.RootExecutionId,
        ParentExecutionId = entry.Hierarchy.ParentExecutionId,
        Depth = entry.Hierarchy.Depth,
        SequenceIndex = entry.Hierarchy.SequenceIndex
      },
      Summary = entry.Summary,
      Details = entry.Details.Select(d => new ExecutionDetailItemDto
      {
        Kind = d.Kind,
        Message = d.Message,
        Attributes = d.Attributes,
        Sensitivity = d.Sensitivity
      }).ToArray(),
      StepOutcomes = entry.StepOutcomes.Select(s => new ExecutionStepOutcomeDto
      {
        StepOrder = s.StepOrder,
        StepType = s.StepType,
        Outcome = s.Outcome,
        ReasonCode = s.ReasonCode,
        ReasonText = s.ReasonText
      }).ToArray()
    };
}
