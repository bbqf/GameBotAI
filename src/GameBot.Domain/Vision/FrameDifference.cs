using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Vision;

/// <summary>
/// Compares two frames (feature 174). The result is the mean absolute difference of the R, G, and B bytes,
/// scaled to 0..1. 0 means the same pixels. 1 means the largest possible change.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FrameDifference {
  /// <summary>
  /// Gives the difference of two frames inside <paramref name="region"/> (the whole frame when null).
  /// The region is cut to the frame. A frame pair of different sizes gives 1. A region with no pixel
  /// in the frame gives 1.
  /// </summary>
  public static double Compute(Bitmap previous, Bitmap current, PixelRegion? region) {
    ArgumentNullException.ThrowIfNull(previous);
    ArgumentNullException.ThrowIfNull(current);
    if (previous.Width != current.Width || previous.Height != current.Height) {
      return 1d;
    }

    var area = region is null
      ? new PixelRegion(0, 0, current.Width, current.Height)
      : region.ClipTo(current.Width, current.Height);
    if (area is null) {
      return 1d;
    }

    var rect = new Rectangle(area.X, area.Y, area.Width, area.Height);
    var dataPrevious = previous.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try {
      var dataCurrent = current.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      try {
        return SumDifference(dataPrevious, dataCurrent, area.Width, area.Height);
      }
      finally {
        current.UnlockBits(dataCurrent);
      }
    }
    finally {
      previous.UnlockBits(dataPrevious);
    }
  }

  private static unsafe double SumDifference(BitmapData a, BitmapData b, int width, int height) {
    long sum = 0;
    for (var y = 0; y < height; y++) {
      var rowA = (byte*)a.Scan0 + (long)y * a.Stride;
      var rowB = (byte*)b.Scan0 + (long)y * b.Stride;
      for (var x = 0; x < width; x++) {
        var i = x * 4;
        sum += Math.Abs(rowA[i] - rowB[i]);
        sum += Math.Abs(rowA[i + 1] - rowB[i + 1]);
        sum += Math.Abs(rowA[i + 2] - rowB[i + 2]);
      }
    }

    return sum / (255d * 3d * width * height);
  }
}
