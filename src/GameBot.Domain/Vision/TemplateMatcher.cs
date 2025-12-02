using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace GameBot.Domain.Vision
{
    public sealed class TemplateMatcher : ITemplateMatcher
    {
        public Task<TemplateMatchResult> MatchAllAsync(Mat screenshot, Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(screenshot);
            ArgumentNullException.ThrowIfNull(templateMat);
            ArgumentNullException.ThrowIfNull(config);
            if (screenshot.Empty() || templateMat.Empty())
                return Task.FromResult(new TemplateMatchResult(Array.Empty<TemplateMatch>(), false));

            if (templateMat.Rows > screenshot.Rows || templateMat.Cols > screenshot.Cols)
                return Task.FromResult(new TemplateMatchResult(Array.Empty<TemplateMatch>(), false));

            using var graySrc = EnsureGrayscale(screenshot);
            using var grayTpl = EnsureGrayscale(templateMat);

            var resultRows = graySrc.Rows - grayTpl.Rows + 1;
            var resultCols = graySrc.Cols - grayTpl.Cols + 1;
            using var result = new Mat(resultRows, resultCols, MatType.CV_32FC1);

            Cv2.MatchTemplate(graySrc, grayTpl, result, TemplateMatchModes.CCoeffNormed);

            // Collect candidates >= threshold, sorted by confidence desc
            var candidates = new List<TemplateMatch>();
            for (int y = 0; y < resultRows; y++)
            {
                for (int x = 0; x < resultCols; x++)
                {
                    var score = result.At<float>(y, x);
                    if (score >= config.Threshold)
                    {
                        var bbox = new BoundingBox(x, y, grayTpl.Cols, grayTpl.Rows);
                        candidates.Add(new TemplateMatch(bbox, score));
                    }
                }
            }

            if (candidates.Count == 0)
                return Task.FromResult(new TemplateMatchResult(Array.Empty<TemplateMatch>(), false));

            // Sort by confidence desc
            candidates.Sort(static (a, b) => b.Confidence.CompareTo(a.Confidence));

            var pruned = Nms.Apply(candidates, config.Overlap, config.MaxResults);
            var limitsHit = pruned.Count >= config.MaxResults && candidates.Count > pruned.Count;

            return Task.FromResult(new TemplateMatchResult(pruned, limitsHit));
        }

        private static Mat EnsureGrayscale(Mat m)
        {
            if (m.Channels() == 1)
                return m.Clone();

            var gray = new Mat();
            switch (m.Channels())
            {
                case 3:
                    Cv2.CvtColor(m, gray, ColorConversionCodes.BGR2GRAY);
                    break;
                case 4:
                    Cv2.CvtColor(m, gray, ColorConversionCodes.BGRA2GRAY);
                    break;
                default:
                    // Fallback: convert to 8U then treat as grayscale
                    m.ConvertTo(gray, MatType.CV_8UC1);
                    break;
            }
            return gray;
        }
    }
}
