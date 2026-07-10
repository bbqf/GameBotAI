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

    private static async Task<IResult> DetectAsync(
        DetectRequest req,
        IReferenceImageStore store,
        ITemplateMatcher matcher,
        IOptions<GameBot.Service.Services.Detections.DetectionOptions> detOpts,
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

      // Convert stored image bytes to Mat
      Mat templateMat;
      // Convert stored bitmap to Mat
      using (var msTpl = new System.IO.MemoryStream()) {
        tplBmp.Save(msTpl, System.Drawing.Imaging.ImageFormat.Png);
        templateMat = Mat.FromImageData(msTpl.ToArray(), ImreadModes.Color);
      }

      // Acquire current screenshot from screen source via trigger infra is not directly available here;
      // For Phase 3 MVP, if ADB screen source is registered, use it; else return empty.
      // To avoid new dependencies, attempt to resolve IScreenSource from DI if present.
      var screenSrc = sp.GetService(typeof(GameBot.Domain.Triggers.Evaluators.IScreenSource)) as GameBot.Domain.Triggers.Evaluators.IScreenSource;
      if (screenSrc is null) {
        // No screen source; return empty results (additive behavior)
        return Results.Ok(new DetectResponse { Matches = new(), LimitsHit = false });
      }

      using var screenshotBmp = screenSrc.GetLatestScreenshot();
      if (screenshotBmp is null) {
        return Results.Ok(new DetectResponse { Matches = new(), LimitsHit = false });
      }

      // Convert screenshot to Mat
      Mat screenshotMat;
      using (var ms = new System.IO.MemoryStream()) {
        screenshotBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        screenshotMat = Mat.FromImageData(ms.ToArray(), ImreadModes.Color);
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
      ImageDetectionsMetrics.Record(elapsedMs, result.Matches.Count);

      // Normalize bbox coordinates
      var resp = new DetectResponse { LimitsHit = result.LimitsHit };
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
      try { templateMat = Mat.FromImageData(bytes, ImreadModes.Color); }
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
