using GameBot.Service.Contracts.Backup;
using GameBot.Service.Services;

namespace GameBot.Service.Endpoints;

internal static class BackupRestoreEndpoints {
  /// <summary>Registers backup and restore routes on the application.</summary>
  public static IEndpointRouteBuilder MapBackupRestoreEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost(ApiRoutes.AuthoringBackup, async (
      BackupRequestDto request,
      BackupService backupService,
      HttpContext ctx,
      CancellationToken ct) => {
      if ((request.CommandIds.Count == 0) && (request.SequenceIds.Count == 0))
        return Results.BadRequest(new { error = "At least one commandId or sequenceId must be provided." });

      ctx.Response.ContentType = "application/zip";
      var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
      ctx.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"gamebot-backup-{timestamp}.zip\"");

      await backupService.CreateBackupAsync(request, ctx.Response.Body, ct).ConfigureAwait(false);
      return Results.Empty;
    })
    .WithName("CreateBackup")
    .WithTags("Backup");

    app.MapPost(ApiRoutes.AuthoringRestoreDryRun, async (
      HttpRequest request,
      BackupService backupService,
      CancellationToken ct) => {
      if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Missing 'archive' file field." });
      IFormCollection form;
      try { form = await request.ReadFormAsync(ct).ConfigureAwait(false); }
      catch (InvalidOperationException) { return Results.BadRequest(new { error = "Missing 'archive' file field." }); }
      if (!form.Files.Any()) {
        return Results.BadRequest(new { error = "Missing 'archive' file field." });
      }

      var file = form.Files["archive"] ?? form.Files[0];
      try {
        using var stream = file.OpenReadStream();
        var report = await backupService.DryRunRestoreAsync(stream, ct).ConfigureAwait(false);
        return Results.Ok(report);
      }
      catch (BackupFormatException ex) {
        return Results.BadRequest(new { error = ex.Message });
      }
    })
    .WithName("DryRunRestore")
    .WithTags("Backup");

    app.MapPost(ApiRoutes.AuthoringRestoreApply, async (
      HttpRequest request,
      BackupService backupService,
      CancellationToken ct) => {
      if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Missing 'archive' file field." });
      IFormCollection form;
      try { form = await request.ReadFormAsync(ct).ConfigureAwait(false); }
      catch (InvalidOperationException) { return Results.BadRequest(new { error = "Missing 'archive' file field." }); }
      if (!form.Files.Any()) {
        return Results.BadRequest(new { error = "Missing 'archive' file field." });
      }

      var file = form.Files["archive"] ?? form.Files[0];
      try {
        using var stream = file.OpenReadStream();
        var result = await backupService.ApplyRestoreAsync(stream, ct).ConfigureAwait(false);
        if (result.RolledBack)
          return Results.Json(result, statusCode: StatusCodes.Status500InternalServerError);
        return Results.Ok(result);
      }
      catch (BackupFormatException ex) {
        return Results.BadRequest(new { error = ex.Message });
      }
    })
    .WithName("ApplyRestore")
    .WithTags("Backup");

    return app;
  }
}
