using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Domain.Vision
{
    public sealed record TemplateMatcherConfig(double Threshold, int MaxResults, double Overlap);

    public sealed record TemplateMatch(BoundingBox BBox, double Confidence);

    public sealed record TemplateMatchResult(IReadOnlyList<TemplateMatch> Matches, bool LimitsHit);

    public interface ITemplateMatcher
    {
        Task<TemplateMatchResult> MatchAllAsync(OpenCvSharp.Mat screenshot, OpenCvSharp.Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default);
    }
}
