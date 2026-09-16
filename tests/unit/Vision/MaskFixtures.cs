using System;
using GameBot.Domain.Vision;
using OpenCvSharp;

namespace GameBot.UnitTests.Vision {
  /// <summary>
  /// Builds the issue-#190 scenario in code: a circular badge whose rectangular crop carries dead
  /// background, rendered over two unlike backdrops plus a control frame with no badge.
  /// </summary>
  /// <remarks>
  /// Everything is generated deterministically from pixel coordinates — no committed binary assets,
  /// no randomness, identical on every machine. The badge art is a hash-based pattern and the
  /// backdrops use a different hash, so no accidental correlation between the two can flatter a
  /// result.
  /// </remarks>
  internal static class MaskFixtures {
    public const int BadgeWidth = 42;
    public const int BadgeHeight = 52;

    /// <summary>Which backdrop a frame or crop sits on. The two differ enough that a template
    /// carrying its source backdrop cannot match over the other one.</summary>
    public enum Backdrop {
      Dark,
      Bright
    }

    /// <summary>
    /// The reference image as an operator would author it: the badge opaque, everything around it
    /// erased to transparency. The transparent pixels are deliberately filled with a colour that
    /// appears nowhere else, so a regression that ignores the mask scores visibly worse.
    /// </summary>
    public static Mat CreateMaskedTemplate(bool uniformInterior = false) {
      var tpl = new Mat(new Size(BadgeWidth, BadgeHeight), MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
      for (var y = 0; y < BadgeHeight; y++) {
        for (var x = 0; x < BadgeWidth; x++) {
          if (IsInsideBadge(x, y)) {
            var c = uniformInterior ? new Vec3b(90, 90, 90) : BadgeColor(x, y);
            tpl.Set(y, x, new Vec4b(c.Item0, c.Item1, c.Item2, 255));
          }
          else {
            // Magenta: never present in any frame, so an unmasked comparison of this template is
            // unmistakably bad rather than accidentally tolerable.
            tpl.Set(y, x, new Vec4b(255, 0, 255, 0));
          }
        }
      }
      return tpl;
    }

    /// <summary>
    /// The reference image as it has to be authored today: a rectangular crop that necessarily
    /// includes whatever backdrop sat behind the badge.
    /// </summary>
    public static Mat CreateOpaqueTemplate(Backdrop backdrop) {
      var tpl = new Mat(new Size(BadgeWidth, BadgeHeight), MatType.CV_8UC3, new Scalar(0, 0, 0));
      for (var y = 0; y < BadgeHeight; y++) {
        for (var x = 0; x < BadgeWidth; x++) {
          tpl.Set(y, x, IsInsideBadge(x, y) ? BadgeColor(x, y) : BackdropColor(backdrop, x, y));
        }
      }
      return tpl;
    }

    /// <summary>
    /// A screen frame on the given backdrop, with the badge painted at <paramref name="badgeAt"/>
    /// or absent entirely (the control frame).
    /// </summary>
    public static Mat CreateFrame(int width, int height, Backdrop backdrop, Point? badgeAt) {
      var frame = new Mat(new Size(width, height), MatType.CV_8UC3, new Scalar(0, 0, 0));
      for (var y = 0; y < height; y++) {
        for (var x = 0; x < width; x++) {
          frame.Set(y, x, BackdropColor(backdrop, x, y));
        }
      }

      if (badgeAt is Point at) {
        for (var y = 0; y < BadgeHeight; y++) {
          for (var x = 0; x < BadgeWidth; x++) {
            if (IsInsideBadge(x, y))
              frame.Set(at.Y + y, at.X + x, BadgeColor(x, y));
          }
        }
      }

      return frame;
    }

    /// <summary>
    /// Repaints the frame pixels that fall under the template's <b>transparent</b> region, leaving
    /// every retained pixel untouched. A masked comparison must not notice.
    /// </summary>
    public static void RepaintUnderMask(Mat frame, Point badgeAt) {
      for (var y = 0; y < BadgeHeight; y++) {
        for (var x = 0; x < BadgeWidth; x++) {
          if (!IsInsideBadge(x, y))
            frame.Set(badgeAt.Y + y, badgeAt.X + x, new Vec3b(7, 231, 19));
        }
      }
    }

    /// <summary>
    /// A frame of one single shade. This is the issue-#196 scenario in its purest form: the dimmed
    /// backdrop this game draws behind every modal is flat, and a flat region under the mask leaves
    /// the correlation with a zero denominator.
    /// </summary>
    public static Mat CreateFlatFrame(int width, int height, int shade) =>
      new(new Size(width, height), MatType.CV_8UC3, new Scalar(shade, shade, shade));

    /// <summary>
    /// A frame whose retained-region standard deviation can be dialled, for walking across the
    /// no-information cutoff.
    /// </summary>
    /// <param name="amplitude">Jitter of ±amplitude shades, discrete uniform.</param>
    /// <param name="everyNth">Apply the jitter to one pixel in <paramref name="everyNth"/>; 1 means
    /// every pixel. Together these give σ ≈ sqrt(amplitude·(amplitude+1) / (3·everyNth)), so
    /// (1,1)→0.82, (2,2)→1.00, (2,1)→1.41, (3,1)→2.00, (4,1)→2.58.</param>
    /// <remarks>
    /// Tests <b>measure</b> the achieved σ with <see cref="MeasureSceneStdUnderMask"/> rather than
    /// trusting that formula — the mask shape and 8-bit clamping both perturb it, and a test that
    /// asserts against a nominal σ it never checked would be asserting against a guess.
    /// </remarks>
    public static Mat CreateGradedFrame(int width, int height, int baseShade, int amplitude, int everyNth = 1) {
      var frame = new Mat(new Size(width, height), MatType.CV_8UC3, new Scalar(baseShade, baseShade, baseShade));
      if (amplitude <= 0) return frame;
      for (var y = 0; y < height; y++) {
        for (var x = 0; x < width; x++) {
          if (everyNth > 1 && (((y * width) + x) % everyNth) != 0) continue;
          var span = (2 * amplitude) + 1;
          var jitter = (int)(Hash(x, y, 0xC2B2AE35u) % (uint)span) - amplitude;
          var v = (byte)Math.Clamp(baseShade + jitter, 0, 255);
          frame.Set(y, x, new Vec3b(v, v, v));
        }
      }
      return frame;
    }

    /// <summary>
    /// The standard deviation, in shade levels, of the frame region that <paramref name="maskedTemplate"/>
    /// retains when placed at the given position — the quantity the no-information rule is stated on.
    /// </summary>
    public static double MeasureSceneStdUnderMask(Mat frame, Mat maskedTemplate, int atX, int atY) {
      using var gray = frame.Channels() == 1 ? frame.Clone() : frame.CvtColor(ColorConversionCodes.BGR2GRAY);
      double sum = 0, sumSq = 0;
      var n = 0;
      for (var y = 0; y < maskedTemplate.Rows; y++) {
        for (var x = 0; x < maskedTemplate.Cols; x++) {
          if (maskedTemplate.At<Vec4b>(y, x).Item3 < TemplateMask.RetainedAlphaThreshold) continue;
          double v = gray.At<byte>(atY + y, atX + x);
          sum += v;
          sumSq += v * v;
          n++;
        }
      }
      if (n == 0) return 0;
      var mean = sum / n;
      return Math.Sqrt(Math.Max(0, (sumSq / n) - (mean * mean)));
    }

    /// <summary>
    /// The masked similarity at one position, computed directly from the definition in double
    /// precision.
    /// </summary>
    /// <remarks>
    /// Deliberately written from the textbook formula — subtract the retained means, correlate,
    /// divide by the product of the retained norms — and <b>not</b> from the production code's
    /// three-correlation formulation. A cross-check that mirrors the implementation it is checking
    /// only proves the implementation agrees with itself.
    /// </remarks>
    public static double ReferenceMaskedScore(Mat frame, Mat maskedTemplate, int atX, int atY) {
      using var grayFrame = frame.Channels() == 1 ? frame.Clone() : frame.CvtColor(ColorConversionCodes.BGR2GRAY);
      using var grayTpl = maskedTemplate.CvtColor(ColorConversionCodes.BGRA2GRAY);

      double sumT = 0, sumI = 0;
      var n = 0;
      for (var y = 0; y < maskedTemplate.Rows; y++) {
        for (var x = 0; x < maskedTemplate.Cols; x++) {
          if (maskedTemplate.At<Vec4b>(y, x).Item3 < TemplateMask.RetainedAlphaThreshold) continue;
          sumT += grayTpl.At<byte>(y, x);
          sumI += grayFrame.At<byte>(atY + y, atX + x);
          n++;
        }
      }
      if (n == 0) return 0;

      var meanT = sumT / n;
      var meanI = sumI / n;
      double num = 0, normT = 0, normI = 0;
      for (var y = 0; y < maskedTemplate.Rows; y++) {
        for (var x = 0; x < maskedTemplate.Cols; x++) {
          if (maskedTemplate.At<Vec4b>(y, x).Item3 < TemplateMask.RetainedAlphaThreshold) continue;
          var dt = grayTpl.At<byte>(y, x) - meanT;
          var di = grayFrame.At<byte>(atY + y, atX + x) - meanI;
          num += dt * di;
          normT += dt * dt;
          normI += di * di;
        }
      }
      var den = Math.Sqrt(normT * normI);
      return den > 0 ? num / den : 0;
    }

    /// <summary>Counts the badge's opaque pixels, for asserting a retained-pixel count.</summary>
    public static int CountBadgePixels() {
      var count = 0;
      for (var y = 0; y < BadgeHeight; y++)
        for (var x = 0; x < BadgeWidth; x++)
          if (IsInsideBadge(x, y)) count++;
      return count;
    }

    private static bool IsInsideBadge(int x, int y) {
      var cx = (BadgeWidth - 1) / 2.0;
      var cy = (BadgeHeight - 1) / 2.0;
      var radius = Math.Min(cx, cy);
      var dx = (x - cx) / radius;
      var dy = (y - cy) / radius;
      return (dx * dx) + (dy * dy) <= 1.0;
    }

    private static Vec3b BadgeColor(int x, int y) {
      var v = (byte)(Hash(x + 1, y + 1, 0x9E3779B9) % 256);
      return new Vec3b(v, (byte)(255 - v), (byte)((v * 3) % 256));
    }

    private static Vec3b BackdropColor(Backdrop backdrop, int x, int y) {
      var baseValue = backdrop == Backdrop.Dark ? 40 : 205;
      var jitter = (int)(Hash(x, y, 0x85EBCA6B) % 16) - 8;
      var v = (byte)Math.Clamp(baseValue + jitter, 0, 255);
      return new Vec3b(v, v, v);
    }

    /// <summary>
    /// A cheap integer hash. Deliberately not a linear function of x and y: a linear texture could
    /// correlate with the linear badge pattern and make a masked match look better than it is.
    /// </summary>
    private static uint Hash(int x, int y, uint seed) {
      unchecked {
        var h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ seed;
        h ^= h >> 13;
        h *= 0x5BD1E995u;
        h ^= h >> 15;
        return h;
      }
    }
  }
}
