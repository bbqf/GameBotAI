using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.IO;
using System.Linq;
using GameBot.Domain.Images;
namespace GameBot.Service.Endpoints;

[SupportedOSPlatform("windows")]
internal static class EmulatorImageEndpoints {
  private const int MinBounds = 16;

  public static IEndpointRouteBuilder MapEmulatorImageEndpoints(this IEndpointRouteBuilder app) {
    // Capture emulator screenshot (served from background capture service cache)
    app.MapGet(ApiRoutes.EmulatorScreenshot, async (HttpContext ctx, ISessionManager sessions, CaptureSessionStore captures, ILogger<EmulatorImageLoggingTag> logger, string? sessionId = null, string? serial = null) => {
      var captureService = ctx.RequestServices.GetService<BackgroundScreenCaptureService>();
      // Feature 079 (FR-022..FR-024): resolve the device explicitly. Before this, an unqualified
      // request with several sessions open returned an arbitrary one, so an operator could crop a
      // reference image from the wrong emulator without noticing.
      GameBot.Domain.Sessions.EmulatorSession? session;
      if (!string.IsNullOrWhiteSpace(sessionId)) {
        session = sessions.GetSession(sessionId);
      }
      else if (!string.IsNullOrWhiteSpace(serial)) {
        session = FindSessionBySerial(sessions, serial);
        if (session is null) {
          return Results.Json(new { error = "session_not_found", message = $"No running session is bound to device '{serial}'." }, statusCode: StatusCodes.Status404NotFound);
        }
      }
      else {
        var running = RunningSessions(sessions);
        if (running.Count > 1) {
          return Results.Json(new { error = "ambiguous_session", message = $"{running.Count} device sessions are active; specify sessionId or serial." }, statusCode: StatusCodes.Status409Conflict);
        }
        session = running.Count == 1 ? running[0] : null;
      }

      if (session is null) {
        return Results.Json(new { error = "emulator_unavailable", hint = "No running emulator session found. Start the emulator and retry." }, statusCode: StatusCodes.Status503ServiceUnavailable);
      }

      // Try background capture cache first
      var frame = captureService?.GetCachedFrame(session.Id);
      if (frame is not null) {
        var capture = captures.Add(frame.PngBytes);
        ctx.Response.Headers["X-Capture-Id"] = capture.Id;
        EmulatorImageLog.CaptureSucceeded(logger, capture.Id, capture.Width, capture.Height);
        return Results.File(frame.PngBytes, "image/png");
      }

      // Fallback: direct ADB capture (before background loop starts, or when service unavailable)
      try {
        var pngBytes = await sessions.GetSnapshotAsync(session.Id).ConfigureAwait(false);
        var capture = captures.Add(pngBytes);
        ctx.Response.Headers["X-Capture-Id"] = capture.Id;
        EmulatorImageLog.CaptureSucceeded(logger, capture.Id, capture.Width, capture.Height);
        return Results.File(pngBytes, "image/png");
      }
      catch {
        return Results.Json(new { error = "emulator_unavailable", hint = "No cached screenshot available and direct capture failed." }, statusCode: StatusCodes.Status503ServiceUnavailable);
      }
    }).WithName("GetEmulatorScreenshot").WithTags("Emulators");

    // Crop and save image
    app.MapPost(ApiRoutes.ImageCrop, async (CropRequest req, CaptureSessionStore captures, ImageCropper cropper, IImageRepository repo, ImageStorageOptions storageOptions, IImageCaptureMetrics metrics, ILogger<EmulatorImageLoggingTag> logger, CancellationToken ct) => {
      if (req.Bounds is null) {
        return Results.BadRequest(new { error = "bounds_required" });
      }
      if (string.IsNullOrWhiteSpace(req.Name)) {
        return Results.BadRequest(new { error = "name_required" });
      }
      if (req.Bounds.Width < MinBounds || req.Bounds.Height < MinBounds) {
        return Results.BadRequest(new { error = "bounds_too_small", hint = $"Minimum size is {MinBounds}x{MinBounds}" });
      }
      if (req.SourceCaptureId is null || !captures.TryGet(req.SourceCaptureId, out var capture)) {
        return Results.NotFound(new { error = "capture_missing", hint = "Capture expired or not found. Capture a new screenshot and retry." });
      }

      var sw = Stopwatch.StartNew();
      try {
        var (png, withinOnePixel) = ImageCropper.Crop(capture!, new CropBounds(req.Bounds.X, req.Bounds.Y, req.Bounds.Width, req.Bounds.Height));
        var filename = req.Name + ".png";
        await repo.SaveAsync(req.Name, new MemoryStream(png, writable: false), "image/png", filename, req.Overwrite, ct).ConfigureAwait(false);
        sw.Stop();
        metrics.RecordCaptureResult((long)sw.Elapsed.TotalMilliseconds, success: true, withinOnePixel: withinOnePixel);
        var storagePath = Path.Combine(storageOptions.Root, filename);
        EmulatorImageLog.CropSaved(logger, req.Name, filename, storagePath, req.Bounds.X, req.Bounds.Y, req.Bounds.Width, req.Bounds.Height, withinOnePixel, sw.ElapsedMilliseconds);
        return Results.Created(ApiRoutes.ImageCrop, new { name = req.Name, fileName = filename, storagePath, bounds = req.Bounds });
      }
      catch (ArgumentOutOfRangeException ex) {
        sw.Stop();
        metrics.RecordCaptureResult((long)sw.Elapsed.TotalMilliseconds, success: false, withinOnePixel: false);
        EmulatorImageLog.CropInvalid(logger, ex.Message);
        return Results.BadRequest(new { error = "bounds_out_of_range", hint = "Selection must stay within the captured image.", captureSize = new { width = capture!.Width, height = capture!.Height } });
      }
      catch (InvalidOperationException ex) {
        sw.Stop();
        metrics.RecordCaptureResult((long)sw.Elapsed.TotalMilliseconds, success: false, withinOnePixel: false);
        EmulatorImageLog.CropConflict(logger, ex.Message);
        return Results.Conflict(new { error = "conflict", message = ex.Message });
      }
      catch (ArgumentException ex) {
        sw.Stop();
        metrics.RecordCaptureResult((long)sw.Elapsed.TotalMilliseconds, success: false, withinOnePixel: false);
        EmulatorImageLog.CropInvalid(logger, ex.Message);
        return Results.BadRequest(new { error = "invalid_request", message = ex.Message });
      }
    }).WithName("CropImage").WithTags("Images");

    return app;
  }

  /// <summary>
  /// Every running session, in listing order (feature 079). Replaces the old <c>PickSession</c>, whose
  /// <c>FirstOrDefault</c> chain silently chose an arbitrary emulator once more than one queue was
  /// running. Sessions with no bound serial are included: in stub/non-ADB mode that is every session,
  /// and excluding them would break single-session capture there.
  /// </summary>
  private static List<GameBot.Domain.Sessions.EmulatorSession> RunningSessions(ISessionManager sessions) =>
    sessions.ListSessions()
      .Where(s => s.Status == GameBot.Domain.Sessions.SessionStatus.Running)
      .ToList();

  /// <summary>The running session bound to <paramref name="serial"/>, or null when there is none.</summary>
  private static GameBot.Domain.Sessions.EmulatorSession? FindSessionBySerial(ISessionManager sessions, string serial) =>
    RunningSessions(sessions)
      .Find(s => !string.IsNullOrWhiteSpace(s.DeviceSerial)
                 && string.Equals(s.DeviceSerial, serial.Trim(), StringComparison.OrdinalIgnoreCase));
}

internal sealed class CropRequest {
  public string Name { get; set; } = string.Empty;
  public bool Overwrite { get; set; }
  public CropRequestBounds? Bounds { get; set; }
  public string? SourceCaptureId { get; set; }
}

internal sealed class CropRequestBounds {
  public int X { get; set; }
  public int Y { get; set; }
  public int Width { get; set; }
  public int Height { get; set; }
}

internal sealed class EmulatorImageLoggingTag { }

internal static partial class EmulatorImageLog {
  [LoggerMessage(EventId = 52020, Level = LogLevel.Warning, Message = "Failed to capture emulator screenshot")]
  public static partial void CaptureFailed(ILogger logger, Exception ex);

  [LoggerMessage(EventId = 52023, Level = LogLevel.Information, Message = "Capture stored with id {CaptureId} size {Width}x{Height}")]
  public static partial void CaptureSucceeded(ILogger logger, string CaptureId, int Width, int Height);

  [LoggerMessage(EventId = 52024, Level = LogLevel.Information, Message = "Crop saved name {Name} file {FileName} at {Path} bounds {X},{Y} {Width}x{Height} withinOnePixel={WithinOnePixel} durationMs={DurationMs}")]
  public static partial void CropSaved(ILogger logger, string Name, string FileName, string Path, int X, int Y, int Width, int Height, bool WithinOnePixel, long DurationMs);

  [LoggerMessage(EventId = 52021, Level = LogLevel.Warning, Message = "Failed to crop or save image: {Message}")]
  public static partial void CropConflict(ILogger logger, string Message);

  [LoggerMessage(EventId = 52022, Level = LogLevel.Warning, Message = "Invalid crop request: {Message}")]
  public static partial void CropInvalid(ILogger logger, string Message);
}
