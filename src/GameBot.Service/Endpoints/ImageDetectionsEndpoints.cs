using System;
using System.Linq;
using System.Threading;
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
        public static IEndpointRouteBuilder MapImageDetectionsEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/images/detect", async (DetectRequest req,
                IReferenceImageStore store,
                ITemplateMatcher matcher,
                IOptions<GameBot.Service.Services.Detections.DetectionOptions> detOpts,
                CancellationToken ct) =>
            {
                var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("GameBot.Service.ImageDetections");
                var (ok, error) = ImageDetectionsValidation.ValidateRequest(req);
                if (!ok)
                {
                    logger.LogDetectInvalid(error!);
                    return Results.BadRequest(new { code = "invalid_request", message = error });
                }

                var id = req.ReferenceImageId!;
                var threshold = req.Threshold ?? detOpts.Value.Threshold;
                var maxResults = req.MaxResults ?? detOpts.Value.MaxResults;
                var overlap = req.Overlap ?? detOpts.Value.Overlap;

                ImageDetectionsEndpointComponent.LogDetectStart(logger, id, threshold, maxResults, overlap);

                if (!store.TryGet(id, out var tplBmp) || tplBmp is null)
                {
                    ImageDetectionsEndpointComponent.LogDetectNotFound(logger, id);
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
                var result = await matcher.MatchAllAsync(screenshotMat, templateMat, cfg, ct).ConfigureAwait(false);
                start.Stop();
                ImageDetectionsEndpointComponent.LogDetectResults(logger, result.Matches.Count, result.LimitsHit, (long)start.Elapsed.TotalMilliseconds);

                // Normalize bbox coordinates
                var w = screenshotMat.Cols;
                var h = screenshotMat.Rows;
                var resp = new DetectResponse
                {
                    LimitsHit = result.LimitsHit
                };
                foreach (var m in result.Matches)
                {
                    resp.Matches.Add(new MatchResult
                    {
                        Confidence = Math.Clamp(m.Confidence, 0, 1),
                        Bbox = new NormalizedRect
                        {
                            X = Math.Clamp((double)m.BBox.X / w, 0, 1),
                            Y = Math.Clamp((double)m.BBox.Y / h, 0, 1),
                            Width = Math.Clamp((double)m.BBox.Width / w, 0, 1),
                            Height = Math.Clamp((double)m.BBox.Height / h, 0, 1)
                        }
                    });
                }

                return Results.Ok(resp);
            })
            .WithTags("GameBot.Service")
            .WithName("DetectImageMatches");

            return endpoints;
        }
    }
}
