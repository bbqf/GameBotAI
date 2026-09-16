using System;
using OpenCvSharp;

namespace GameBot.Domain.Vision {
  /// <summary>
  /// Computes a normalised-correlation score map restricted to a template's retained pixels.
  /// </summary>
  /// <remarks>
  /// <para>
  /// This is <see cref="TemplateMatchModes.CCoeffNormed"/> — the measure every existing detection
  /// threshold is calibrated against — evaluated over the masked pixels only. With an all-ones mask
  /// it reduces algebraically to exactly that measure, which is why a masked score sits on the same
  /// 0..1 scale and an operator can reuse a threshold unchanged.
  /// </para>
  /// <para>
  /// OpenCV's own <c>matchTemplate</c> mask parameter cannot be used here: it supports only
  /// <c>TM_SQDIFF</c> and <c>TM_CCORR_NORMED</c>, and switching measure would shift every score in
  /// the system.
  /// </para>
  /// <para>
  /// With mask <c>m</c> in {0,1}, template <c>T</c>, window <c>I</c>, and <c>n = sum(m)</c>:
  /// <code>
  /// num   = sum(m*T*I) - sum(m*T)*sum(m*I)/n
  /// varT  = sum(m*T*T) - sum(m*T)^2/n
  /// varI  = sum(m*I*I) - sum(m*I)^2/n
  /// score = num / sqrt(varT * varI)
  /// </code>
  /// The three image-dependent sums are each a plain cross-correlation over the template window, so
  /// each is one <see cref="TemplateMatchModes.CCorr"/> call. Cost is therefore a small multiple of
  /// the unmasked path rather than a multiple of the template area.
  /// </para>
  /// <para>
  /// <b>Degenerate windows.</b> Where <c>varI</c> is zero or near it the quotient is meaningless,
  /// and OpenCV's division does <i>not</i> yield zero there — it yields infinity. Both the screen
  /// window and the template's own retained region are therefore required to carry at least
  /// <see cref="MinimumShadeStdDev"/> of shade variation before they are scored at all, and the
  /// finished map is clamped into [-1, 1]. See feature 090 (issue #196).
  /// </para>
  /// </remarks>
  internal static class MaskedTemplateMatch {
    /// <summary>
    /// The least shade variation, on the 0-255 scale, that a retained region must carry for the
    /// correlation to mean anything. Below it the region is reported as carrying no information.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fixed and deliberately not configurable (feature 090, FR-016): it is a threshold in units an
    /// operator cannot observe, and one that would have to be recalibrated per screen. A wrong
    /// setting silently re-arms the hazard this guard exists to remove.
    /// </para>
    /// <para>
    /// The value sits in a wide gap. The smallest non-zero standard deviation 8-bit content can
    /// carry is one pixel differing by one shade — 0.0277 for a 1304-pixel region, which is
    /// evidence of nothing. The lowest real screen content that must keep being scored is the
    /// dimmed modal backdrop from issue #196, at 2.43. One shade level lies between them with
    /// margin on both sides, and is already what <c>ImageMatchEvaluator</c> means by a constant
    /// template, so the codebase keeps a single definition of "featureless".
    /// </para>
    /// </remarks>
    public const double MinimumShadeStdDev = 1.0;

    /// <summary>
    /// Builds the masked score map.
    /// </summary>
    /// <param name="graySrc">Single-channel 8-bit screenshot.</param>
    /// <param name="grayTpl">Single-channel 8-bit template.</param>
    /// <param name="mask">CV_8UC1 mask, 255 where retained.</param>
    /// <param name="retainedCount">Number of retained pixels; must be positive.</param>
    /// <param name="noInformationPositionCount">
    /// On return, how many candidate positions were scored zero because the screen region under the
    /// mask carried less than <see cref="MinimumShadeStdDev"/> of shade variation.
    /// </param>
    /// <returns>
    /// A CV_32FC1 score map the caller owns, every value within [-1, 1], or <c>null</c> when the
    /// template's retained region itself carries too little variation in shade for the correlation
    /// to be defined — reported as no match rather than as an error or an arbitrary score
    /// (089 FR-009, widened by 090 FR-018).
    /// </returns>
    public static Mat? ComputeScoreMap(Mat graySrc, Mat grayTpl, Mat mask, int retainedCount,
                                       out int noInformationPositionCount) {
      noInformationPositionCount = 0;
      ArgumentNullException.ThrowIfNull(graySrc);
      ArgumentNullException.ThrowIfNull(grayTpl);
      ArgumentNullException.ThrowIfNull(mask);
      if (retainedCount <= 0)
        return null;

      var n = (double)retainedCount;

      // The rule is stated on the standard deviation, which is independent of how many pixels the
      // mask retains, so the same screen content is judged the same way by a large and a small
      // reference image (FR-005). The accumulated variance below is n * sigma^2, so the equivalent
      // cutoff on it is n * sigma_min^2.
      var minimumVariance = n * MinimumShadeStdDev * MinimumShadeStdDev;

      // maskF is 1.0 where retained, so multiplying by it both zeroes the masked-out pixels and
      // makes it the kernel that sums image values over the retained region.
      using var maskF = new Mat();
      mask.ConvertTo(maskF, MatType.CV_32FC1, 1.0 / 255.0);

      using var tplF = new Mat();
      grayTpl.ConvertTo(tplF, MatType.CV_32FC1);

      using var maskedTpl = new Mat();
      Cv2.Multiply(tplF, maskF, maskedTpl);

      using var maskedTplSq = new Mat();
      Cv2.Multiply(maskedTpl, tplF, maskedTplSq);

      var sumT = Cv2.Sum(maskedTpl).Val0;
      var sumT2 = Cv2.Sum(maskedTplSq).Val0;
      var varT = sumT2 - (sumT * sumT / n);
      // A reference image whose retained pixels are all but the same shade correlates with almost
      // any patch of screen — the mirror of the defect this guard's screen-side twin fixes, and the
      // one this repository met before as the `pns-never-matches` sentinel (FR-018).
      if (varT < minimumVariance)
        return null;

      using var srcF = new Mat();
      graySrc.ConvertTo(srcF, MatType.CV_32FC1);
      using var srcSq = new Mat();
      Cv2.Multiply(srcF, srcF, srcSq);

      var rows = graySrc.Rows - grayTpl.Rows + 1;
      var cols = graySrc.Cols - grayTpl.Cols + 1;

      using var sumTI = new Mat(rows, cols, MatType.CV_32FC1);
      using var sumI = new Mat(rows, cols, MatType.CV_32FC1);
      using var sumI2 = new Mat(rows, cols, MatType.CV_32FC1);
      Cv2.MatchTemplate(srcF, maskedTpl, sumTI, TemplateMatchModes.CCorr);
      Cv2.MatchTemplate(srcF, maskF, sumI, TemplateMatchModes.CCorr);
      Cv2.MatchTemplate(srcSq, maskF, sumI2, TemplateMatchModes.CCorr);

      // varI = sum(m*I*I) - sum(m*I)^2/n, forced to exactly zero wherever the window carries less
      // than MinimumShadeStdDev of variation: such a window has no correlation to report, and
      // scoring it zero says "no evidence" rather than inventing a similarity. Tozero keeps
      // src > thresh and zeroes the rest.
      using var sumISquaredOverN = new Mat();
      Cv2.Multiply(sumI, sumI, sumISquaredOverN, 1.0 / n);
      using var varI = new Mat();
      Cv2.Subtract(sumI2, sumISquaredOverN, varI);
      Cv2.Threshold(varI, varI, minimumVariance, 0, ThresholdTypes.Tozero);
      using var sqrtVarI = new Mat();
      Cv2.Sqrt(varI, sqrtVarI);

      using var numerator = new Mat();
      Cv2.AddWeighted(sumTI, 1.0, sumI, -sumT / n, 0.0, numerator);

      // Make the division total before performing it.
      //
      // This code used to assert that Cv2.Divide yields 0 wherever the divisor is 0. For CV_32FC1
      // it does not: it yields +/-Infinity, and Normalization.ClampConfidence then reported every
      // one of those as exactly 1.0 — a perfect match on a dimmed modal backdrop, which is the
      // whole of issue #196. A flat 400x300 frame produced 69,524 infinite positions.
      //
      // The repair has to happen on the operands, not on the quotient: Infinity * 0 is NaN, so a
      // score map cannot be cleaned up after the fact. Where the denominator is unusable the
      // numerator is set to 0 and the denominator to 1, making the quotient exactly 0 there.
      using var noInformationFloat = new Mat();
      Cv2.Threshold(varI, noInformationFloat, 0, 255, ThresholdTypes.BinaryInv);
      using var noInformation = new Mat();
      noInformationFloat.ConvertTo(noInformation, MatType.CV_8UC1);
      noInformationPositionCount = Cv2.CountNonZero(noInformation);
      numerator.SetTo(0.0, noInformation);
      sqrtVarI.SetTo(1.0, noInformation);

      var scores = new Mat();
      Cv2.Divide(numerator, sqrtVarI, scores, 1.0 / Math.Sqrt(varT));

      // The measure's range is [-1, 1], and it is guaranteed here rather than at the API boundary.
      // A clamp applied only on the way out is what made an impossible score look plausible for a
      // release; it disguises an out-of-range value instead of preventing one.
      Cv2.Min(scores, 1.0, scores);
      Cv2.Max(scores, -1.0, scores);
      return scores;
    }
  }
}
