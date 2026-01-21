using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.IO;
using System.Linq;
using GameBot.Domain.Images;

namespace GameBot.Service.Endpoints;

[SupportedOSPlatform("windows")]
internal static class EmulatorImageEndpoints
{
    private const int MinBounds = 16;

    public static IEndpointRouteBuilder MapEmulatorImageEndpoints(this IEndpointRouteBuilder app)
    {
        // Capture emulator screenshot
        app.MapGet(ApiRoutes.EmulatorScreenshot, async (HttpContext ctx, ISessionManager sessions, CaptureSessionStore captures, ILogger<EmulatorImageLoggingTag> logger, CancellationToken ct) =>
        {
            var session = PickSession(sessions);
            if (session is null)
            {
                return Results.Json(new { error = "emulator_unavailable", hint = "No running emulator session found. Start the emulator and retry." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var png = await sessions.GetSnapshotAsync(session.Id, ct).ConfigureAwait(false);
                var capture = captures.Add(png);
                ctx.Response.Headers["X-Capture-Id"] = capture.Id;
                return Results.File(png, "image/png");
            }
            catch (Exception ex)
            {
                EmulatorImageLog.CaptureFailed(logger, ex);
                return Results.Json(new { error = "emulator_unavailable", hint = "Emulator not ready. Ensure it is running and retry capture." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("GetEmulatorScreenshot").WithTags("Emulators");

        // Crop and save image
        app.MapPost(ApiRoutes.ImageCrop, async (CropRequest req, CaptureSessionStore captures, ImageCropper cropper, IImageRepository repo, ImageStorageOptions storageOptions, IImageCaptureMetrics metrics, ILogger<EmulatorImageLoggingTag> logger, CancellationToken ct) =>
        {
            if (req.Bounds is null)
            {
                return Results.BadRequest(new { error = "bounds_required" });
            }
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new { error = "name_required" });
            }
            if (req.Bounds.Width < MinBounds || req.Bounds.Height < MinBounds)
            {
                return Results.BadRequest(new { error = "bounds_too_small", hint = $"Minimum size is {MinBounds}x{MinBounds}" });
            }
            if (req.SourceCaptureId is null || !captures.TryGet(req.SourceCaptureId, out var capture))
            {
                return Results.NotFound(new { error = "capture_missing", hint = "Capture expired or not found. Capture a new screenshot and retry." });
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var (png, withinOnePixel) = ImageCropper.Crop(capture!, new CropBounds(req.Bounds.X, req.Bounds.Y, req.Bounds.Width, req.Bounds.Height));
                var filename = req.Name + ".png";
                await repo.SaveAsync(req.Name, new MemoryStream(png, writable: false), "image/png", filename, req.Overwrite, ct).ConfigureAwait(false);
                sw.Stop();
                metrics.RecordCaptureResult((long)sw.Elapsed.TotalMilliseconds, success: true, withinOnePixel: withinOnePixel);
                var storagePath = Path.Combine(storageOptions.Root, filename);
                return Results.Created(ApiRoutes.ImageCrop, new { name = req.Name, fileName = filename, storagePath, bounds = req.Bounds });
            }
            catch (ArgumentOutOfRangeException ex)
            {
                sw.Stop();
                metrics.RecordCaptureResult((long)sw.Elapsed.TotalMilliseconds, success: false, withinOnePixel: false);
                EmulatorImageLog.CropInvalid(logger, ex.Message);
                return Results.BadRequest(new { error = "bounds_out_of_range", hint = "Selection must stay within the captured image.", captureSize = new { width = capture!.Width, height = capture!.Height } });
            }
            catch (InvalidOperationException ex)
            {
                sw.Stop();
                metrics.RecordCaptureResult((long)sw.Elapsed.TotalMilliseconds, success: false, withinOnePixel: false);
                EmulatorImageLog.CropConflict(logger, ex.Message);
                return Results.Conflict(new { error = "conflict", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                sw.Stop();
                metrics.RecordCaptureResult((long)sw.Elapsed.TotalMilliseconds, success: false, withinOnePixel: false);
                EmulatorImageLog.CropInvalid(logger, ex.Message);
                return Results.BadRequest(new { error = "invalid_request", message = ex.Message });
            }
        }).WithName("CropImage").WithTags("Images");

        return app;
    }

    private static GameBot.Domain.Sessions.EmulatorSession? PickSession(ISessionManager sessions)
    {
        var all = sessions.ListSessions();
        return all.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.DeviceSerial) && s.Status == GameBot.Domain.Sessions.SessionStatus.Running)
            ?? all.FirstOrDefault(s => s.Status == GameBot.Domain.Sessions.SessionStatus.Running)
            ?? all.FirstOrDefault();
    }
}

internal sealed class CropRequest
{
    public string Name { get; set; } = string.Empty;
    public bool Overwrite { get; set; }
    public CropRequestBounds? Bounds { get; set; }
    public string? SourceCaptureId { get; set; }
}

internal sealed class CropRequestBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

internal sealed class EmulatorImageLoggingTag { }

internal static partial class EmulatorImageLog
{
    [LoggerMessage(EventId = 52020, Level = LogLevel.Warning, Message = "Failed to capture emulator screenshot")]
    public static partial void CaptureFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 52021, Level = LogLevel.Warning, Message = "Failed to crop or save image: {Message}")]
    public static partial void CropConflict(ILogger logger, string Message);

    [LoggerMessage(EventId = 52022, Level = LogLevel.Warning, Message = "Invalid crop request: {Message}")]
    public static partial void CropInvalid(ILogger logger, string Message);
}
