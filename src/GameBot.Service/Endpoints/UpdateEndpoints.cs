using System.Text.Json;
using GameBot.Domain.Updates;
using GameBot.Service.Models;
using GameBot.Service.Services.Updates;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The update routes (feature 131): status, check, and install. Handlers are named methods, not
/// inline lambdas, because the taint analyzers cost more on large lambdas (see VersioningEndpoints).
/// </summary>
internal static class UpdateEndpoints {
  private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web);

  public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder app) {
    app.MapGet(ApiRoutes.Update + "/status", GetStatus)
      .WithTags("Update")
      .WithName("GetUpdateStatus")
      .WithSummary("Get the update state")
      .WithDescription(
        "Returns the installed version, the last check, the active attempt, and the result of the last attempt before a restart. "
        + "canInstallHere is true only when the request comes from the bot PC and the bot runs from an installed folder.")
      .Produces<UpdateStatusResponse>(StatusCodes.Status200OK);

    app.MapPost(ApiRoutes.Update + "/check", CheckAsync)
      .WithTags("Update")
      .WithName("CheckForUpdate")
      .WithSummary("Check for a newer version")
      .WithDescription(
        "Reads the latest release and compares it with the installed version. A failed check returns 200 with status checkFailed.")
      .Produces<UpdateCheckDto>(StatusCodes.Status200OK)
      .Produces<UpdateErrorResponse>(StatusCodes.Status409Conflict);

    app.MapPost(ApiRoutes.Update + "/install", InstallAsync)
      .WithTags("Update")
      .WithName("InstallUpdate")
      .WithSummary("Install the newest version")
      .WithDescription(
        "Starts the update. Only a request from the bot PC is allowed. All active queues stop at once, so confirmStopQueues must be true. "
        + "Later failures show in GET /api/update/status.")
      .Accepts<UpdateInstallRequest>("application/json")
      .Produces<UpdateInstallAccepted>(StatusCodes.Status202Accepted)
      .Produces<UpdateErrorResponse>(StatusCodes.Status400BadRequest)
      .Produces<UpdateErrorResponse>(StatusCodes.Status403Forbidden)
      .Produces<UpdateErrorResponse>(StatusCodes.Status409Conflict)
      .Produces<UpdateErrorResponse>(StatusCodes.Status422UnprocessableEntity);
    return app;
  }

  private static IResult GetStatus(
      HttpContext context,
      UpdateCheckService check,
      UpdateCoordinator coordinator,
      UpdateResultReporter reporter,
      IInstalledVersionProvider installed,
      IInstallLocationGuard locationGuard) {
    var blocked = BlockedReason(LoopbackGuard.IsLocalRequest(context), locationGuard);
    var response = new UpdateStatusResponse {
      InstalledVersion = installed.GetInstalledVersion().ToString(),
      LastCheck = check.LastCheck is null ? null : ToDto(check.LastCheck),
      Attempt = coordinator.CurrentAttempt is null ? null : ToDto(coordinator.CurrentAttempt),
      LastResult = reporter.LastResult is null ? null : ToDto(reporter.LastResult),
      CanInstallHere = blocked is null,
      InstallBlockedReason = blocked
    };
    return Results.Ok(response);
  }

  private static async Task<IResult> CheckAsync(UpdateCheckService check, UpdateCoordinator coordinator, CancellationToken ct) {
    if (coordinator.IsInstallActive) {
      return Error(StatusCodes.Status409Conflict, "update_in_progress", "An update is in progress.", "Wait until the update ends.");
    }

    var result = await check.CheckAsync(ct).ConfigureAwait(false);
    return Results.Ok(ToDto(result));
  }

  private static async Task<IResult> InstallAsync(
      HttpContext context,
      UpdateCoordinator coordinator,
      IInstallLocationGuard locationGuard,
      CancellationToken ct) {
    // Order of the checks: the contract in contracts/update-api.md. The first failed check decides.
    if (!LoopbackGuard.IsLocalRequest(context)) {
      return Error(StatusCodes.Status403Forbidden, "update_local_only", "Install from the bot PC.", "Open the UI on the PC that runs GameBot.");
    }
    if (!locationGuard.IsInstalledHere()) {
      return Error(StatusCodes.Status409Conflict, "update_not_installed", "Update works only for an installed bot", "Install GameBot with the installer.");
    }

    UpdateInstallRequest? request;
    try {
      request = await JsonSerializer.DeserializeAsync<UpdateInstallRequest>(context.Request.Body, BodyOptions, ct).ConfigureAwait(false);
    }
    catch (JsonException) {
      request = null;
    }

    if (request is null || !request.ConfirmStopQueues) {
      return Error(StatusCodes.Status400BadRequest, "update_confirmation_required", "Confirm that all active queues stop.", "Send confirmStopQueues as true after the user confirms.");
    }

    var start = coordinator.TryStartInstall(request.TargetVersion ?? string.Empty);
    return start.Outcome switch {
      InstallStartOutcome.Started => Results.Json(
        new UpdateInstallAccepted { AttemptId = start.Attempt!.AttemptId.ToString("D"), State = StateName(start.Attempt.State) },
        statusCode: StatusCodes.Status202Accepted),
      InstallStartOutcome.NotAvailable => Error(StatusCodes.Status400BadRequest, "update_not_available", start.Message!, "Check for an update again."),
      InstallStartOutcome.InProgress => Error(StatusCodes.Status409Conflict, "update_in_progress", start.Message!, "Wait until the update ends."),
      _ => Error(StatusCodes.Status422UnprocessableEntity, "update_disk_space", start.Message!, "Free disk space on the data drive, then try again.")
    };
  }

  /// <summary><c>remote</c> has priority over <c>notInstalled</c>.</summary>
  private static string? BlockedReason(bool isLocal, IInstallLocationGuard locationGuard) {
    if (!isLocal) {
      return "remote";
    }

    return locationGuard.IsInstalledHere() ? null : "notInstalled";
  }

  private static IResult Error(int status, string code, string message, string? hint) =>
    Results.Json(
      new UpdateErrorResponse { Error = new UpdateErrorDto { Code = code, Message = message, Hint = hint } },
      statusCode: status);

  internal static UpdateCheckDto ToDto(UpdateCheckResult result) => new() {
    Status = StateName(result.Status),
    InstalledVersion = result.InstalledVersion.ToString(),
    LatestVersion = result.LatestVersion?.ToString(),
    Notes = result.Notes,
    CheckedAtUtc = result.CheckedAtUtc,
    Error = result.Error is null
      ? null
      : new UpdateErrorDto { Code = result.Error.Code, Message = result.Error.Message, Hint = result.Error.Hint }
  };

  internal static UpdateAttemptDto ToDto(UpdateAttempt attempt) => new() {
    AttemptId = attempt.AttemptId.ToString("D"),
    State = StateName(attempt.State),
    TargetVersion = attempt.TargetVersion,
    FromVersion = attempt.FromVersion,
    StartedAtUtc = attempt.StartedAtUtc,
    FinishedAtUtc = attempt.FinishedAtUtc,
    ErrorCode = attempt.ErrorCode,
    ErrorMessage = attempt.ErrorMessage,
    ErrorHint = attempt.ErrorHint,
    MsiexecExitCode = attempt.MsiexecExitCode,
    LogPath = attempt.LogPath
  };

  /// <summary>The camelCase name of an enum value, for example <c>updateAvailable</c>.</summary>
  private static string StateName<TEnum>(TEnum value) where TEnum : struct, Enum {
    var name = value.ToString();
    return char.ToLowerInvariant(name[0]) + name.Substring(1);
  }
}
