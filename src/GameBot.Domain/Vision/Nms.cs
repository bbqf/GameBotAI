using System;
using System.Collections.Generic;

namespace GameBot.Domain.Vision
{
    internal static class Nms
    {
        public static List<TemplateMatch> Apply(IReadOnlyList<TemplateMatch> candidates, double overlap, int maxResults)
        {
            var results = new List<TemplateMatch>(Math.Min(candidates.Count, maxResults));
            var used = new bool[candidates.Count];

            for (int i = 0; i < candidates.Count && results.Count < maxResults; i++)
            {
                if (used[i]) continue;
                var best = candidates[i];
                results.Add(best);
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    if (used[j]) continue;
                    var iou = BoundingBox.IoU(best.BBox, candidates[j].BBox);
                    if (iou > overlap) used[j] = true;
                }
            }

            return results;
        }
    }
}
