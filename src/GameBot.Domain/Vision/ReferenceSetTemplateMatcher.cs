using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace GameBot.Domain.Vision {
  /// <summary>
  /// Matches a named reference image together with its alternates as one detection target
  /// (feature 097, issue #192): a match on any reference counts as a match on the named image.
  /// </summary>
  /// <remarks>
  /// <para>The <c>templateMat</c> passed to <see cref="MatchAllAsync"/> is the primary. The inner matcher
  /// scores each reference on its own terms (size, mask), with the caller's config, so no reference's
  /// score differs from what it would score alone. Candidates are merged in the same order the single
  /// template matcher uses — confidence descending — with ties going to the primary, then alternates in
  /// registered order, then position; the existing overlap rule and result cap are then re-applied.</para>
  /// <para>Only construct this when alternates exist. An image without alternates must keep using the
  /// inner matcher directly, which is what keeps every existing score and result bit-identical.</para>
  /// </remarks>
  public sealed partial class ReferenceSetTemplateMatcher : ITemplateMatcher {
    private readonly ITemplateMatcher _inner;
    private readonly string _primaryId;
    private readonly IReadOnlyList<(string Id, Mat Template)> _alternates;
    private readonly ILogger? _logger;

    /// <param name="inner">The single-template matcher that scores each reference.</param>
    /// <param name="primaryId">Id of the named image, whose Mat is passed to <see cref="MatchAllAsync"/>.</param>
    /// <param name="alternates">Alternate ids and templates in registered order. Not owned: the caller disposes them.</param>
    /// <param name="logger">Receives which reference matched when an alternate wins.</param>
    public ReferenceSetTemplateMatcher(ITemplateMatcher inner, string primaryId, IReadOnlyList<(string Id, Mat Template)> alternates, ILogger? logger = null) {
      _inner = inner ?? throw new ArgumentNullException(nameof(inner));
      _primaryId = primaryId ?? throw new ArgumentNullException(nameof(primaryId));
      _alternates = alternates ?? throw new ArgumentNullException(nameof(alternates));
      _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TemplateMatchResult> MatchAllAsync(Mat screenshot, Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default) {
      ArgumentNullException.ThrowIfNull(config);
      cancellationToken.ThrowIfCancellationRequested();

      var primary = await _inner.MatchAllAsync(screenshot, templateMat, config, cancellationToken).ConfigureAwait(false);
      var candidates = new List<(TemplateMatch Match, int Rank)>();
      Collect(candidates, primary, _primaryId, 0);
      var limitsHit = primary.LimitsHit;
      var noInformation = primary.NoInformationPositionCount;

      for (var i = 0; i < _alternates.Count; i++) {
        cancellationToken.ThrowIfCancellationRequested();
        var (id, template) = _alternates[i];
        var result = await _inner.MatchAllAsync(screenshot, template, config, cancellationToken).ConfigureAwait(false);
        Collect(candidates, result, id, i + 1);
        limitsHit |= result.LimitsHit;
        noInformation += result.NoInformationPositionCount;
      }

      candidates.Sort(static (a, b) => {
        var byConf = b.Match.Confidence.CompareTo(a.Match.Confidence);
        if (byConf != 0) return byConf;
        var byRank = a.Rank.CompareTo(b.Rank);
        if (byRank != 0) return byRank;
        var byX = a.Match.BBox.X.CompareTo(b.Match.BBox.X); if (byX != 0) return byX;
        var byY = a.Match.BBox.Y.CompareTo(b.Match.BBox.Y); if (byY != 0) return byY;
        var byW = a.Match.BBox.Width.CompareTo(b.Match.BBox.Width); if (byW != 0) return byW;
        return a.Match.BBox.Height.CompareTo(b.Match.BBox.Height);
      });

      var ordered = candidates.ConvertAll(c => c.Match);
      var pruned = Nms.Apply(ordered, config.Overlap, config.MaxResults);
      limitsHit |= pruned.Count >= config.MaxResults && ordered.Count > pruned.Count;

      if (_logger is not null && pruned.Count > 0 && !string.Equals(pruned[0].ReferenceId, _primaryId, StringComparison.Ordinal)) {
        LogAlternateMatched(_logger, _primaryId, pruned[0].ReferenceId, pruned[0].Confidence);
      }

      return new TemplateMatchResult(pruned, limitsHit) {
        Masked = primary.Masked,
        RetainedPixelCount = primary.RetainedPixelCount,
        NoInformationPositionCount = noInformation
      };
    }

    private static void Collect(List<(TemplateMatch Match, int Rank)> into, TemplateMatchResult result, string id, int rank) {
      foreach (var m in result.Matches) {
        into.Add((m with { ReferenceId = id }, rank));
      }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Detection of '{PrimaryId}' matched alternate reference '{ReferenceId}' (confidence {Confidence:F3})")]
    private static partial void LogAlternateMatched(ILogger logger, string primaryId, string? referenceId, double confidence);
  }
}
