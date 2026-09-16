using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Service;
using GameBot.Service.Endpoints.Dto;
using GameBot.Service.Services;
using GameBot.Domain.Vision;
using GameBot.Domain.Images;
using GameBot.Domain.Triggers.Evaluators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;

namespace GameBot.Service.Endpoints {
  // Handlers are named methods rather than inline lambdas: the Roslyn taint
  // analyzers (CA3xxx) analyze lambdas as part of the containing method, and
  // their cost grows super-linearly with method body size.
  internal static class ImageDetectionsEndpoints {
    private static string SanitizeForLog(string? value) {
      if (string.IsNullOrEmpty(value)) return string.Empty;
      return value.Replace("\r", string.Empty, StringComparison.Ordinal)
                  .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    public static IEndpointRouteBuilder MapImageDetectionsEndpoints(this IEndpointRouteBuilder endpoints) {
      endpoints.MapPost(ApiRoutes.ImageDetect, DetectAsync)
        .WithTags("Images")
        .WithName("DetectImageMatches");

      endpoints.MapPost(ApiRoutes.ImageDetectAll, DetectAllAsync)
        .WithTags("Images")
        .WithName("DetectAllImageMatches");

      return endpoints;
    }

    /// <summary>
    /// Which screen a detection request should be measured against, or why none could be determined
    /// (feature 085, issue #176).
    /// </summary>
    /// <remarks>
    /// A failure is carried as a status + code + message rather than as an absent frame the caller
    /// might quietly treat as "nothing matched". That conflation is exactly the defect this feature
    /// fixes, so the type is shaped to make it awkward to reintroduce.
    /// </remarks>
    internal sealed record FrameResolution(byte[]? Png, int Status, string? Code, string? Message) {
      /// <summary>A screen was determined; measure against this PNG.</summary>
      public static FrameResolution Frame(byte[] png) => new(png, StatusCodes.Status200OK, null, null);

      /// <summary>An explicitly named target does not resolve. Never falls back to another screen.</summary>
      public static FrameResolution NotFound(string code, string message) =>
        new(null, StatusCodes.Status404NotFound, code, message);

      /// <summary>Several devices are in play and the request named none of them.</summary>
      public static FrameResolution Ambiguous(string message) =>
        new(null, StatusCodes.Status409Conflict, "ambiguous_session", message);

      /// <summary>No screen is obtainable at all, however the request is phrased.</summary>
      public static FrameResolution Unavailable(string message) =>
        new(null, StatusCodes.Status503ServiceUnavailable, "emulator_unavailable", message);
    }

    /// <summary>
    /// Resolves the screen a detection request means (feature 085, issue #176).
    /// </summary>
    /// <remarks>
    /// An explicitly named <c>captureId</c> or <c>sessionId</c> wins outright, including over the
    /// ambient device context. With neither named, the singleton <see cref="IScreenSource"/>
    /// resolves the screen exactly as it always has: ambient context, then the sole running session.
    ///
    /// <para>The unresolved case is detected by <c>GetLatestScreenshot()</c> returning null — never
    /// by counting sessions beforehand. Stub hosts (<c>GAMEBOT_USE_ADB=false</c>) serve a fixed
    /// bitmap with zero sessions running, so a pre-emptive session count would fail every existing
    /// contract test while fixing nothing about the real defect. Session state is read only after a
    /// null frame, and only to decide which of the two failures to report.</para>
    ///
    /// <para>Screen sources are registered only inside the <c>OperatingSystem.IsWindows()</c> guard,
    /// so both lookups here are optional and a missing registration reports "unavailable" rather
    /// than throwing.</para>
    /// </remarks>
    /// <param name="req">The detection request, whose target fields may both be absent.</param>
    /// <param name="captures">Store backing <c>captureId</c> lookups.</param>
    /// <param name="sp">Used for the optional screen-source and session-manager lookups.</param>
    /// <returns>A frame to measure, or the failure to report. Never an empty success.</returns>
    internal static FrameResolution ResolveFrame(DetectRequest req, CaptureSessionStore captures, IServiceProvider sp) {
      // Blank counts as absent, so a client sending "" stays on the implicit path it used before.
      var captureId = string.IsNullOrWhiteSpace(req.CaptureId) ? null : req.CaptureId;
      var sessionId = string.IsNullOrWhiteSpace(req.SessionId) ? null : req.SessionId;

      if (captureId is not null) {
        return captures.TryGet(captureId, out var capture) && capture is not null
          ? FrameResolution.Frame(capture.Png)
          : FrameResolution.NotFound("capture_not_found", "capture not found or expired");
      }

      var sessions = sp.GetService(typeof(GameBot.Emulator.Session.ISessionManager))
        as GameBot.Emulator.Session.ISessionManager;

      if (sessionId is not null) {
        if (sessions?.GetSession(sessionId) is null) {
          return FrameResolution.NotFound("session_not_found", "No session is known by that id.");
        }
        var factory = sp.GetService(typeof(GameBot.Domain.Triggers.Evaluators.IScreenSourceFactory))
          as GameBot.Domain.Triggers.Evaluators.IScreenSourceFactory;
        return ToFrame(factory?.ForSession(sessionId)?.GetLatestScreenshot())
          ?? FrameResolution.Unavailable("No screenshot has been captured for that session yet.");
      }

      var screenSrc = sp.GetService(typeof(GameBot.Domain.Triggers.Evaluators.IScreenSource))
        as GameBot.Domain.Triggers.Evaluators.IScreenSource;
      var resolved = ToFrame(screenSrc?.GetLatestScreenshot());
      if (resolved is not null) return resolved;

      // Only now, with the failure already established, ask why. Same predicate as
      // BackgroundCaptureScreenSource.ResolveSessionId, so the diagnosis matches the refusal.
      var running = sessions?.ListSessions()
        .Count(s => !string.IsNullOrWhiteSpace(s.DeviceSerial)
                    && s.Status == GameBot.Domain.Sessions.SessionStatus.Running) ?? 0;
      return running > 1
        ? FrameResolution.Ambiguous($"{running} device sessions are active; specify sessionId or captureId.")
        : FrameResolution.Unavailable("No running emulator session found. Start the emulator and retry.");
    }

    /// <summary>Encodes a resolved screenshot as PNG, or returns null when there was none.</summary>
    private static FrameResolution? ToFrame(System.Drawing.Bitmap? bmp) {
      if (bmp is null) return null;
      using (bmp) {
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return FrameResolution.Frame(ms.ToArray());
      }
    }

    private static async Task<IResult> DetectAsync(
        DetectRequest req,
        IReferenceImageStore store,
        ITemplateMatcher matcher,
        IOptions<GameBot.Service.Services.Detections.DetectionOptions> detOpts,
        CaptureSessionStore captures,
        IServiceProvider sp,
        CancellationToken ct) {
      var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("GameBot.Service.ImageDetections");
      var (ok, error) = ImageDetectionsValidation.ValidateRequest(req);
      if (!ok) {
        logger.LogDetectInvalid(SanitizeForLog(error));
        return Results.BadRequest(new { code = "invalid_request", message = error });
      }

      var id = req.ReferenceImageId!;
      var opts = detOpts.Value;
      var threshold = req.Threshold ?? opts.Threshold;
      if (!ImageDetectionsValidation.ValidateThreshold(threshold)) threshold = ImageDetectionsValidation.DefaultThreshold;

      var maxResultsRaw = req.MaxResults ?? opts.MaxResults;
      var maxResults = ImageDetectionsValidation.ValidateMaxResults(maxResultsRaw) ? maxResultsRaw : ImageDetectionsValidation.DefaultMaxResults;

      var overlap = req.Overlap ?? opts.Overlap;
      if (!ImageDetectionsValidation.ValidateOverlap(overlap)) overlap = ImageDetectionsValidation.DefaultOverlap;

      var safeId = SanitizeForLog(id);
      ImageDetectionsEndpointComponent.LogDetectStart(logger, safeId, threshold, maxResults, overlap);

      if (!store.TryGet(id, out var tplBmp) || tplBmp is null) {
        ImageDetectionsEndpointComponent.LogDetectNotFound(logger, safeId);
        return Results.NotFound(new { code = "not_found", message = "reference image not found" });
      }

      // Feature 085 (issue #176): decide which screen this request means *before* loading the
      // template, so a refusal costs nothing and leaks nothing. Previously an unresolvable screen
      // was answered with an empty match array — a fabricated "absent" indistinguishable from a real
      // one, which silently disarmed every absence probe as soon as a second emulator was running.
      var frame = ResolveFrame(req, captures, sp);
      if (frame.Png is null) {
        ImageDetectionsEndpointComponent.LogDetectUnresolvedScreen(logger, SanitizeForLog(frame.Code), safeId);
        return Results.Json(new { code = frame.Code, message = frame.Message }, statusCode: frame.Status);
      }

      Mat screenshotMat;
      try {
        screenshotMat = Mat.FromImageData(frame.Png, ImreadModes.Color);
      }
      catch {
        // A frame we cannot decode is still a failure to measure, never an empty match set.
        ImageDetectionsEndpointComponent.LogDetectUnresolvedScreen(logger, "undecodable_frame", safeId);
        return Results.Json(
          new { code = "emulator_unavailable", message = "The screenshot for this request could not be decoded." },
          statusCode: StatusCodes.Status503ServiceUnavailable);
      }

      // Convert stored image bytes to Mat. The decode preserves a transparency channel, so a
      // masked reference image is compared on its retained pixels only (feature 089).
      Mat templateMat;
      using (var msTpl = new System.IO.MemoryStream()) {
        tplBmp.Save(msTpl, System.Drawing.Imaging.ImageFormat.Png);
        templateMat = GameBot.Domain.Vision.TemplateImageDecoder.Decode(msTpl.ToArray());
      }

      var cfg = new TemplateMatcherConfig(threshold, maxResults, overlap);
      var start = System.Diagnostics.Stopwatch.StartNew();
      TemplateMatchResult result;
      long elapsedMs;
      try {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, detOpts.Value.TimeoutMs)));
        result = await matcher.MatchAllAsync(screenshotMat, templateMat, cfg, timeoutCts.Token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        start.Stop();
        elapsedMs = (long)start.Elapsed.TotalMilliseconds;
        ImageDetectionsEndpointComponent.LogDetectResults(logger, 0, true, elapsedMs);
        ImageDetectionsMetrics.Record(elapsedMs, 0);
        var empty = new DetectResponse { LimitsHit = true };
        return Results.Ok(empty);
      }
      start.Stop();
      elapsedMs = (long)start.Elapsed.TotalMilliseconds;
      ImageDetectionsEndpointComponent.LogDetectResults(logger, result.Matches.Count, result.LimitsHit, elapsedMs);
      ImageDetectionsEndpointComponent.LogDetectMask(logger, safeId, result.Masked, result.RetainedPixelCount);
      ImageDetectionsMetrics.Record(elapsedMs, result.Matches.Count);

      // Normalize bbox coordinates
      var resp = new DetectResponse {
        LimitsHit = result.LimitsHit,
        Masked = result.Masked,
        RetainedPixelCount = result.RetainedPixelCount
      };
      foreach (var m in result.Matches) {
        GameBot.Domain.Vision.Normalization.NormalizeRect(m.BBox.X, m.BBox.Y, m.BBox.Width, m.BBox.Height, screenshotMat.Cols, screenshotMat.Rows,
            out var nx, out var ny, out var nw, out var nh);
        resp.Matches.Add(new MatchResult {
          TemplateId = id,
          Score = GameBot.Domain.Vision.Normalization.ClampConfidence(m.Confidence),
          Confidence = GameBot.Domain.Vision.Normalization.ClampConfidence(m.Confidence),
          X = nx,
          Y = ny,
          Width = nw,
          Height = nh,
          Overlap = overlap,
          Bbox = new NormalizedRect { X = nx, Y = ny, Width = nw, Height = nh }
        });
      }

      return Results.Ok(resp);
    }

    private static async Task<IResult> DetectAllAsync(
        DetectAllRequest req,
        CaptureSessionStore captures,
        IImageRepository imageRepo,
        ITemplateMatcher matcher,
        IOptions<GameBot.Service.Services.Detections.DetectionOptions> detOpts,
        CancellationToken ct) {
      if (string.IsNullOrWhiteSpace(req.CaptureId)) {
        return Results.BadRequest(new { code = "invalid_request", message = "captureId is required" });
      }

      if (!captures.TryGet(req.CaptureId, out var capture) || capture is null) {
        return Results.NotFound(new { code = "not_found", message = "capture not found or expired" });
      }

      Mat screenshotMat;
      try {
        screenshotMat = Mat.FromImageData(capture.Png, ImreadModes.Color);
      }
      catch {
        return Results.Problem("Failed to decode screenshot", statusCode: StatusCodes.Status503ServiceUnavailable);
      }

      var opts = detOpts.Value;
      var cfg = new TemplateMatcherConfig(opts.Threshold, opts.MaxResults, opts.Overlap);

      IReadOnlyCollection<string> ids;
      try {
        ids = await imageRepo.ListIdsAsync(ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        screenshotMat.Dispose();
        return Results.Problem("Request cancelled", statusCode: StatusCodes.Status503ServiceUnavailable);
      }

      if (ids.Count == 0) {
        screenshotMat.Dispose();
        return Results.Ok(new DetectAllResponse());
      }

      // CA2025 false positive: Task.WhenAll below completes every task (even when
      // some fault) before either Dispose site runs, so no task can observe a
      // disposed screenshotMat. The rule doesn't model WhenAll and fires on any
      // disposable passed into a task that is not awaited at the call site.
#pragma warning disable CA2025
      var matchTasks = ids.Select(id => MatchTemplateAsync(id, imageRepo, matcher, screenshotMat, cfg, ct)).ToList();
#pragma warning restore CA2025

      (string id, TemplateMatchResult? result)[] allResults;
      try {
        allResults = await Task.WhenAll(matchTasks).ConfigureAwait(false);
      }
      catch {
        screenshotMat.Dispose();
        return Results.Problem("Detection failed", statusCode: StatusCodes.Status503ServiceUnavailable);
      }
      screenshotMat.Dispose();

      var resp = new DetectAllResponse();
      foreach (var (id, result) in allResults) {
        if (result is null) continue;
        foreach (var m in result.Matches) {
          resp.Matches.Add(new DetectAllMatch {
            ImageId = id,
            ImageName = id,
            X = m.BBox.X,
            Y = m.BBox.Y,
            Width = m.BBox.Width,
            Height = m.BBox.Height,
            Confidence = GameBot.Domain.Vision.Normalization.ClampConfidence(m.Confidence)
          });
        }
      }

      return Results.Ok(resp);
    }

    private static async Task<(string id, TemplateMatchResult? result)> MatchTemplateAsync(
        string id,
        IImageRepository imageRepo,
        ITemplateMatcher matcher,
        Mat screenshotMat,
        TemplateMatcherConfig cfg,
        CancellationToken ct) {
      Stream? stream;
      try {
        stream = await imageRepo.OpenReadAsync(id, ct).ConfigureAwait(false);
      }
      catch { return (id, (TemplateMatchResult?)null); }
      if (stream is null) return (id, (TemplateMatchResult?)null);

      byte[] bytes;
      try {
        using (stream)
        using (var ms = new MemoryStream()) {
          await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
          bytes = ms.ToArray();
        }
      }
      catch { return (id, (TemplateMatchResult?)null); }

      Mat templateMat;
      try { templateMat = GameBot.Domain.Vision.TemplateImageDecoder.Decode(bytes); }
      catch { return (id, (TemplateMatchResult?)null); }

      TemplateMatchResult result;
      try {
        using (templateMat)
          result = await matcher.MatchAllAsync(screenshotMat, templateMat, cfg, ct).ConfigureAwait(false);
      }
      catch { return (id, (TemplateMatchResult?)null); }

      return (id, (TemplateMatchResult?)result);
    }
  }
}
