using System;
using System.Linq;
using System.Threading;
using GameBot.Service;
using GameBot.Service.Endpoints.Dto;
using GameBot.Domain.Vision;
using GameBot.Domain.Triggers.Evaluators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;

namespace GameBot.Service.Endpoints
{
    internal static class ImageDetectionsEndpoints
    {
        private static string SanitizeForLog(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r", string.Empty, StringComparison.Ordinal)
                        .Replace("\n", string.Empty, StringComparison.Ordinal);
        }

        public static IEndpointRouteBuilder MapImageDetectionsEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost(ApiRoutes.ImageDetect, async (DetectRequest req,
                IReferenceImageStore store,
                ITemplateMatcher matcher,
                IOptions<GameBot.Service.Services.Detections.DetectionOptions> detOpts,
                CancellationToken ct) =>
            {
                var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("GameBot.Service.ImageDetections");
                var (ok, error) = ImageDetectionsValidation.ValidateRequest(req);
                if (!ok)
                {
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

                if (!store.TryGet(id, out var tplBmp) || tplBmp is null)
                {
                    ImageDetectionsEndpointComponent.LogDetectNotFound(logger, safeId);
                    return Results.NotFound(new { code = "not_found", message = "reference image not found" });
                }

                // Convert stored image bytes to Mat
                Mat templateMat;
                // Convert stored bitmap to Mat
                using (var msTpl = new System.IO.MemoryStream())
                {
                    tplBmp.Save(msTpl, System.Drawing.Imaging.ImageFormat.Png);
                    templateMat = Mat.FromImageData(msTpl.ToArray(), ImreadModes.Color);
                }

                // Acquire current screenshot from screen source via trigger infra is not directly available here;
                // For Phase 3 MVP, if ADB screen source is registered, use it; else return empty.
                // To avoid new dependencies, attempt to resolve IScreenSource from DI if present.
                var sp = endpoints.ServiceProvider;
                var screenSrc = sp.GetService(typeof(GameBot.Domain.Triggers.Evaluators.IScreenSource)) as GameBot.Domain.Triggers.Evaluators.IScreenSource;
                if (screenSrc is null)
                {
                    // No screen source; return empty results (additive behavior)
                    return Results.Ok(new DetectResponse { Matches = new(), LimitsHit = false });
                }

                using var screenshotBmp = screenSrc.GetLatestScreenshot();
                if (screenshotBmp is null)
                {
                    return Results.Ok(new DetectResponse { Matches = new(), LimitsHit = false });
                }

                // Convert screenshot to Mat
                Mat screenshotMat;
                using (var ms = new System.IO.MemoryStream())
                {
                    screenshotBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    screenshotMat = Mat.FromImageData(ms.ToArray(), ImreadModes.Color);
                }

                var cfg = new TemplateMatcherConfig(threshold, maxResults, overlap);
                var start = System.Diagnostics.Stopwatch.StartNew();
                TemplateMatchResult result;
                long elapsedMs;
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, detOpts.Value.TimeoutMs)));
                    result = await matcher.MatchAllAsync(screenshotMat, templateMat, cfg, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
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
                foreach (var m in result.Matches)
                {
                    GameBot.Domain.Vision.Normalization.NormalizeRect(m.BBox.X, m.BBox.Y, m.BBox.Width, m.BBox.Height, screenshotMat.Cols, screenshotMat.Rows,
                        out var nx, out var ny, out var nw, out var nh);
                    resp.Matches.Add(new MatchResult
                    {
                        Confidence = GameBot.Domain.Vision.Normalization.ClampConfidence(m.Confidence),
                        Bbox = new NormalizedRect { X = nx, Y = ny, Width = nw, Height = nh }
                    });
                }

                return Results.Ok(resp);
            })
            .WithTags("Images")
            .WithName("DetectImageMatches");

            return endpoints;
        }
    }
}
