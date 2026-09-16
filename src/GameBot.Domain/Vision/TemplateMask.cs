using System;
using OpenCvSharp;

namespace GameBot.Domain.Vision {
  /// <summary>
  /// Derives the binary match mask of a template from its transparency channel.
  /// </summary>
  /// <remarks>
  /// A reference image is always a rectangle, but the thing on screen often is not — a circular
  /// badge crop carries roughly a third dead background, and normalised correlation scores that
  /// background as part of the target. An operator erases it to transparency; this type turns that
  /// transparency into the set of pixels the matcher is allowed to look at (feature 089, issue
  /// #190).
  /// </remarks>
  public static class TemplateMask {
    /// <summary>
    /// Alpha at or above this value is retained. A pixel more transparent than opaque is treated as
    /// background, so an anti-aliased edge cannot smuggle scenery back into the comparison.
    /// </summary>
    public const int RetainedAlphaThreshold = 128;

    /// <summary>
    /// The smallest retained region that can match anything meaningfully. Below this a template
    /// correlates with almost any patch of screen.
    /// </summary>
    public const int MinimumRetainedPixels = 16;

    /// <summary>
    /// Builds the match mask for <paramref name="template"/>, if it has one.
    /// </summary>
    /// <param name="template">A decoded template, possibly 4-channel BGRA.</param>
    /// <param name="mask">On success, a CV_8UC1 mask: 255 where the pixel is retained, 0 elsewhere. The caller owns it.</param>
    /// <param name="retainedCount">On success, the number of retained pixels.</param>
    /// <returns>True when the template carries a usable mask.</returns>
    /// <remarks>
    /// <para>
    /// Returns false for a template whose alpha is uniformly opaque, and <b>that rejection is what
    /// makes the zero-score-drift guarantee structural</b>. Every reference image in use today is
    /// fully opaque; refusing to call it masked keeps it on the untouched
    /// <see cref="TemplateMatchModes.CCoeffNormed"/> path, so its score is bit-identical to what it
    /// was before this feature existed and every hand-calibrated threshold in every live sequence
    /// stays calibrated (FR-004, FR-005).
    /// </para>
    /// <para>
    /// A fully transparent template returns true with a <paramref name="retainedCount"/> of zero —
    /// it is a mask, just one that describes no target. The matcher reports no match for it rather
    /// than falling back to scoring the colour channels behind the transparency.
    /// </para>
    /// </remarks>
    public static bool TryCreate(Mat? template, out Mat mask, out int retainedCount) {
      mask = default!;
      retainedCount = 0;

      if (template is null || template.Empty())
        return false;
      if (template.Depth() != (int)MatType.CV_8U)
        return false;
      if (template.Channels() != 4)
        return false;

      var planes = Cv2.Split(template);
      try {
        var candidate = new Mat();
        try {
          // Binary thresholding keeps src > thresh, so RetainedAlphaThreshold - 1 retains
          // alpha >= RetainedAlphaThreshold exactly.
          Cv2.Threshold(planes[3], candidate, RetainedAlphaThreshold - 1, 255, ThresholdTypes.Binary);
          var count = Cv2.CountNonZero(candidate);

          if (count == template.Rows * template.Cols) {
            candidate.Dispose();
            return false;
          }

          mask = candidate;
          retainedCount = count;
          return true;
        }
        catch {
          candidate.Dispose();
          throw;
        }
      }
      finally {
        foreach (var plane in planes)
          plane.Dispose();
      }
    }

  }
}
