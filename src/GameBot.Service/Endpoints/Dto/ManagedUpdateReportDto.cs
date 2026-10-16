using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Managed;
using Microsoft.AspNetCore.Http;

namespace GameBot.Service.Endpoints.Dto;

/// <summary>A queue in a managed update report.</summary>
internal sealed record ManagedQueueRefDto {
  /// <summary>The queue id.</summary>
  public required string Id { get; init; }

  /// <summary>The queue name.</summary>
  public required string Name { get; init; }

  /// <summary>The name of the linked template, or null.</summary>
  public string? TemplateName { get; init; }

  /// <summary>For a queue that did not start: the error text. Else null.</summary>
  public string? Error { get; init; }

  internal static ManagedQueueRefDto From(QueueRef queue) => new() { Id = queue.Id, Name = queue.Name, TemplateName = queue.TemplateName, Error = queue.Error };
}

/// <summary>An entry in a managed update report.</summary>
internal sealed record ManagedEntryRefDto {
  /// <summary>The template name.</summary>
  public required string TemplateName { get; init; }

  /// <summary>The sequence name.</summary>
  public required string SequenceName { get; init; }

  /// <summary>The zero-based position of the entry in the template.</summary>
  public int Position { get; init; }

  internal static ManagedEntryRefDto From(EntryRef entry) => new() { TemplateName = entry.TemplateName, SequenceName = entry.SequenceName, Position = entry.Position };
}

/// <summary>One step of a restore.</summary>
internal sealed record ManagedRestoreStepDto {
  /// <summary>The step name.</summary>
  public required string Step { get; init; }

  /// <summary>ok or failed.</summary>
  public required string Outcome { get; init; }

  /// <summary>The error text, or null.</summary>
  public string? Error { get; init; }
}

/// <summary>The restore part of a managed update report.</summary>
internal sealed record ManagedRestoreDto {
  /// <summary>True when the flow ran at least one restore step.</summary>
  public bool Happened { get; init; }

  /// <summary>True when all restore steps worked.</summary>
  public bool Restored { get; init; }

  /// <summary>The restore steps in order.</summary>
  public IReadOnlyList<ManagedRestoreStepDto> Steps { get; init; } = Array.Empty<ManagedRestoreStepDto>();

  /// <summary>The objects that the restore could not put back.</summary>
  public IReadOnlyList<string> FailedObjects { get; init; } = Array.Empty<string>();
}

/// <summary>The report of a managed gamepack call (feature 148).</summary>
internal sealed record ManagedUpdateReportDto {
  /// <summary>done, planned, failed, or rejected.</summary>
  public required string Result { get; init; }

  /// <summary>A reason name, or null for done and planned.</summary>
  public string? Reason { get; init; }

  /// <summary>The error text of a failed call, or null.</summary>
  public string? Detail { get; init; }

  /// <summary>Echo of the dryRun flag.</summary>
  public bool DryRun { get; init; }

  /// <summary>The target gamepack.</summary>
  public GamepackRefDto? Gamepack { get; init; }

  /// <summary>The version that was installed before the call.</summary>
  public string? PreviousVersion { get; init; }

  /// <summary>The queues that the platform stopped (planned for a dry run).</summary>
  public IReadOnlyList<ManagedQueueRefDto> StoppedQueues { get; init; } = Array.Empty<ManagedQueueRefDto>();

  /// <summary>The queues that run again.</summary>
  public IReadOnlyList<ManagedQueueRefDto> RestartedQueues { get; init; } = Array.Empty<ManagedQueueRefDto>();

  /// <summary>The queues that did not start.</summary>
  public IReadOnlyList<ManagedQueueRefDto> NotRestartedQueues { get; init; } = Array.Empty<ManagedQueueRefDto>();

  /// <summary>The id of the stored backup, or null.</summary>
  public string? BackupId { get; init; }

  /// <summary>The number of created objects.</summary>
  public int Created { get; init; }

  /// <summary>The number of updated objects.</summary>
  public int Updated { get; init; }

  /// <summary>The number of removed objects.</summary>
  public int Removed { get; init; }

  /// <summary>The number of user objects taken over (planned for a dry run). 0 when the option is off (feature 149).</summary>
  public int TakenOver { get; init; }

  /// <summary>
  /// The id of the take-over backup (feature 149). Not set for a dry run, for a call that replaced nothing, and for a failed call.
  /// <c>backupId</c> keeps its meaning: the queue template backup.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public string? TakeOverBackupId { get; init; }

  /// <summary>The objects to take over. Set for a dry run with the option on and no problem. Empty otherwise.</summary>
  public IReadOnlyList<GamepackTakeOverEntryDto> TakeOvers { get; init; } = Array.Empty<GamepackTakeOverEntryDto>();

  /// <summary>The new disabled entries.</summary>
  public IReadOnlyList<ManagedEntryRefDto> NewEntries { get; init; } = Array.Empty<ManagedEntryRefDto>();

  /// <summary>The entries whose sequence the new version removed.</summary>
  public IReadOnlyList<ManagedEntryRefDto> RemovedSequenceEntries { get; init; } = Array.Empty<ManagedEntryRefDto>();

  /// <summary>The templates in the request list that the gamepack does not affect.</summary>
  public IReadOnlyList<string> NotAffectedTemplates { get; init; } = Array.Empty<string>();

  /// <summary>All problems of the check.</summary>
  public IReadOnlyList<GamepackProblemDto> Problems { get; init; } = Array.Empty<GamepackProblemDto>();

  /// <summary>The signature block.</summary>
  public GamepackSignatureStatusDto? Signature { get; init; }

  /// <summary>The restore part.</summary>
  public ManagedRestoreDto Restore { get; init; } = new();

  internal static ManagedUpdateReportDto From(ManagedUpdateReport report) {
    ArgumentNullException.ThrowIfNull(report);
    return new ManagedUpdateReportDto {
      Result = report.Result,
      Reason = report.Reason,
      Detail = report.Detail,
      DryRun = report.DryRun,
      Gamepack = report.GamepackName is null ? null : new GamepackRefDto(report.GamepackName, report.GamepackVersion),
      PreviousVersion = report.PreviousVersion,
      StoppedQueues = report.StoppedQueues.Select(ManagedQueueRefDto.From).ToList(),
      RestartedQueues = report.RestartedQueues.Select(ManagedQueueRefDto.From).ToList(),
      NotRestartedQueues = report.NotRestartedQueues.Select(ManagedQueueRefDto.From).ToList(),
      BackupId = report.BackupId,
      Created = report.Created,
      Updated = report.Updated,
      Removed = report.Removed,
      TakenOver = report.TakenOver,
      TakeOverBackupId = report.TakeOverBackupId,
      TakeOvers = report.TakeOvers.Select(GamepackTakeOverEntryDto.From).ToList(),
      NewEntries = report.NewEntries.Select(ManagedEntryRefDto.From).ToList(),
      RemovedSequenceEntries = report.RemovedSequenceEntries.Select(ManagedEntryRefDto.From).ToList(),
      NotAffectedTemplates = report.NotAffectedTemplates,
      Problems = report.Problems.Select(GamepackProblemDto.From).ToList(),
      Signature = report.Signature is null ? null : GamepackSignatureStatusDto.From(report.Signature),
      Restore = new ManagedRestoreDto {
        Happened = report.Restore.Happened,
        Restored = report.Restore.Restored,
        Steps = report.Restore.Steps.Select(s => new ManagedRestoreStepDto { Step = s.Step, Outcome = s.Outcome, Error = s.Error }).ToList(),
        FailedObjects = report.Restore.FailedObjects
      }
    };
  }
}

/// <summary>The error body of a managed gamepack call. It has one reason name.</summary>
internal sealed record ManagedErrorDto {
  /// <summary>One name from the reason list.</summary>
  public required string Reason { get; init; }

  /// <summary>A short text.</summary>
  public string? Message { get; init; }
}

/// <summary>Maps a managed report to an HTTP result (contract status table).</summary>
internal static class ManagedUpdateHttp {
  /// <summary>Maps a report. A dry run gives 200 for file, signature, and validation problems.</summary>
  /// <param name="report">The report.</param>
  internal static IResult ToResult(ManagedUpdateReport report) {
    ArgumentNullException.ThrowIfNull(report);
    var dto = ManagedUpdateReportDto.From(report);
    return report.Result switch {
      ManagedUpdateResults.Done or ManagedUpdateResults.Planned => Results.Ok(dto),
      ManagedUpdateResults.Failed => Results.Json(dto, statusCode: StatusCodes.Status500InternalServerError),
      _ => RejectedResult(report, dto)
    };
  }

  /// <summary>Makes an error body result.</summary>
  /// <param name="status">The HTTP status.</param>
  /// <param name="reason">A reason name.</param>
  /// <param name="message">A short text.</param>
  internal static IResult Error(int status, string reason, string message) =>
    Results.Json(new ManagedErrorDto { Reason = reason, Message = message }, statusCode: status);

  private static IResult RejectedResult(ManagedUpdateReport report, ManagedUpdateReportDto dto) {
    var reason = report.Reason ?? ManagedUpdateReasons.Unexpected;
    switch (reason) {
      case ManagedUpdateReasons.TemplateNotFound:
        return Results.Json(dto, statusCode: StatusCodes.Status400BadRequest);
      case ManagedUpdateReasons.ValidationFailed:
      case ManagedUpdateReasons.SignatureRejected:
      case ManagedUpdateReasons.RollbackRefused:
        return report.DryRun ? Results.Ok(dto) : Results.Json(dto, statusCode: StatusCodes.Status422UnprocessableEntity);
      case ManagedUpdateReasons.InvalidFile:
        return report.DryRun ? Results.Ok(dto) : Error(StatusCodes.Status400BadRequest, reason, "The gamepack file is not valid.");
      case ManagedUpdateReasons.UpdateInProgress:
        return report.DryRun ? Results.Ok(dto) : Error(StatusCodes.Status409Conflict, reason, "A managed call runs on this instance.");
      case ManagedUpdateReasons.InvalidRequest:
        return Error(StatusCodes.Status400BadRequest, reason, "The request is not valid.");
      case ManagedUpdateReasons.GamepackNotFound:
        return Error(StatusCodes.Status404NotFound, reason, "No gamepack has this name.");
      case ManagedUpdateReasons.NoPreviousVersion:
        return Error(StatusCodes.Status409Conflict, reason, "The gamepack has no kept previous version.");
      default:
        return Results.Json(dto, statusCode: StatusCodes.Status500InternalServerError);
    }
  }
}
