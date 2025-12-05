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
            cancellationToken.ThrowIfCancellationRequested();
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
                if ((y & 15) == 0) cancellationToken.ThrowIfCancellationRequested();
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

            // Sort deterministically: confidence desc, then bbox tie-breaker (x, y, width, height asc)
            candidates.Sort(static (a, b) =>
            {
                var byConf = b.Confidence.CompareTo(a.Confidence);
                if (byConf != 0) return byConf;
                var ax = a.BBox.X; var ay = a.BBox.Y; var aw = a.BBox.Width; var ah = a.BBox.Height;
                var bx = b.BBox.X; var by = b.BBox.Y; var bw = b.BBox.Width; var bh = b.BBox.Height;
                var byX = ax.CompareTo(bx); if (byX != 0) return byX;
                var byY = ay.CompareTo(by); if (byY != 0) return byY;
                var byW = aw.CompareTo(bw); if (byW != 0) return byW;
                return ah.CompareTo(bh);
            });

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
