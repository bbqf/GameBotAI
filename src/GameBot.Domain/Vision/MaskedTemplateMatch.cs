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
  /// </remarks>
  internal static class MaskedTemplateMatch {
    /// <summary>
    /// Builds the masked score map.
    /// </summary>
    /// <param name="graySrc">Single-channel 8-bit screenshot.</param>
    /// <param name="grayTpl">Single-channel 8-bit template.</param>
    /// <param name="mask">CV_8UC1 mask, 255 where retained.</param>
    /// <param name="retainedCount">Number of retained pixels; must be positive.</param>
    /// <returns>
    /// A CV_32FC1 score map the caller owns, or <c>null</c> when the retained region has no
    /// variation in shade and the correlation is therefore undefined — reported as no match rather
    /// than as an error or an arbitrary score (FR-009).
    /// </returns>
    public static Mat? ComputeScoreMap(Mat graySrc, Mat grayTpl, Mat mask, int retainedCount) {
      ArgumentNullException.ThrowIfNull(graySrc);
      ArgumentNullException.ThrowIfNull(grayTpl);
      ArgumentNullException.ThrowIfNull(mask);
      if (retainedCount <= 0)
        return null;

      var n = (double)retainedCount;

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
      if (varT <= 0)
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

      // varI = sum(m*I*I) - sum(m*I)^2/n, clamped at zero: a window with no variation under the
      // mask has an undefined correlation, and scoring it zero says "no evidence" rather than
      // inventing a similarity.
      using var sumISquaredOverN = new Mat();
      Cv2.Multiply(sumI, sumI, sumISquaredOverN, 1.0 / n);
      using var varI = new Mat();
      Cv2.Subtract(sumI2, sumISquaredOverN, varI);
      Cv2.Threshold(varI, varI, 0, 0, ThresholdTypes.Tozero);
      using var sqrtVarI = new Mat();
      Cv2.Sqrt(varI, sqrtVarI);

      using var numerator = new Mat();
      Cv2.AddWeighted(sumTI, 1.0, sumI, -sumT / n, 0.0, numerator);

      // Cv2.Divide yields 0 wherever the divisor is 0, so a zero-variance window scores 0 without a
      // separate branch.
      var scores = new Mat();
      Cv2.Divide(numerator, sqrtVarI, scores, 1.0 / Math.Sqrt(varT));
      return scores;
    }
  }
}
