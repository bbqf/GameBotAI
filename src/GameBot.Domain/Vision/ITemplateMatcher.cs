using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Domain.Vision {
  public sealed record TemplateMatcherConfig(double Threshold, int MaxResults, double Overlap);

  public sealed record TemplateMatch(BoundingBox BBox, double Confidence);

  /// <param name="Matches">Matches at or above the configured threshold, best first.</param>
  /// <param name="LimitsHit">True when results were truncated by <c>MaxResults</c> or a timeout.</param>
  public sealed record TemplateMatchResult(IReadOnlyList<TemplateMatch> Matches, bool LimitsHit) {
    /// <summary>
    /// True when the template carried a transparency mask and only its retained pixels were
    /// compared. False for every template without a usable mask, which is scored exactly as it was
    /// before feature 089.
    /// </summary>
    /// <remarks>
    /// Added as an init-only member rather than a positional parameter so existing construction
    /// sites and test doubles keep compiling.
    /// </remarks>
    public bool Masked { get; init; }

    /// <summary>
    /// How many template pixels the comparison actually used — the pixels <b>kept</b>, never the
    /// pixels masked out. Zero when <see cref="Masked"/> is false.
    /// </summary>
    public int RetainedPixelCount { get; init; }

    /// <summary>
    /// How many candidate positions the no-information rule scored zero because the screen region
    /// under the mask carried too little detail to correlate against (feature 090, issue #196).
    /// Counts positions <b>suppressed</b>, never positions scored. Zero for an unmasked comparison,
    /// and for a masked comparison over content that is everywhere detailed.
    /// </summary>
    /// <remarks>
    /// Domain-only diagnostic: it is deliberately absent from every response DTO. An operator sees
    /// it through the detect endpoint's log, so that a detection which used to match and now
    /// correctly does not can be told apart from one that simply found nothing.
    /// </remarks>
    public int NoInformationPositionCount { get; init; }
  }

  public interface ITemplateMatcher {
    Task<TemplateMatchResult> MatchAllAsync(OpenCvSharp.Mat screenshot, OpenCvSharp.Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default);
  }
}
