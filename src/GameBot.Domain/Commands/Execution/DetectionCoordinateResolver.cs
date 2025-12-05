using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using GameBot.Domain.Vision;
using OpenCvSharp;

namespace GameBot.Domain.Commands.Execution
{
    [SupportedOSPlatform("windows")]
    public sealed class DetectionCoordinateResolver
    {
        private readonly ITemplateMatcher _matcher;

        public DetectionCoordinateResolver(ITemplateMatcher matcher)
        {
            _matcher = matcher;
        }

        public ResolvedCoordinate? ResolveCenter(DetectionTarget target, Mat screenMat, Mat templateMat, TemplateMatcherConfig config, out string? error)
        {
            error = null;
            ArgumentNullException.ThrowIfNull(target);
            if (screenMat == null || templateMat == null || config == null)
            {
                error = "screen/template/config is null";
                return null;
            }

            var result = _matcher.MatchAllAsync(screenMat, templateMat, config).GetAwaiter().GetResult();
            var passing = new List<(BoundingBox box, double score)>();
            foreach (var d in result.Matches)
            {
                if (d.Confidence >= target.Confidence)
                {
                    passing.Add((d.BBox, d.Confidence));
                }
            }

            if (passing.Count == 0)
            {
                error = "no detection above threshold";
                return null;
            }

            if (passing.Count > 1)
            {
                error = $"multiple detections ({passing.Count}) above threshold";
                return null;
            }

            var (box, score) = passing[0];
            var centerX = box.X + box.Width / 2;
            var centerY = box.Y + box.Height / 2;

            var x = centerX + target.OffsetX;
            var y = centerY + target.OffsetY;

            // clamp
            var screenWidth = screenMat.Cols;
            var screenHeight = screenMat.Rows;
            if (x < 0) x = 0; else if (x >= screenWidth) x = screenWidth - 1;
            if (y < 0) y = 0; else if (y >= screenHeight) y = screenHeight - 1;

            return new ResolvedCoordinate(x, y, score, target.ReferenceImageId, box);
        }
    }
}
