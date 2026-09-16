using System;
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
