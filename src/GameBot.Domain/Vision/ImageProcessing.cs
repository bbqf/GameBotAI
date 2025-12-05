using System.Runtime.Versioning;
using System.Drawing;
using System.Drawing.Imaging;

namespace GameBot.Domain.Vision
{
  [SupportedOSPlatform("windows")]
  public static class ImageProcessing
  {
    public static GrayImage ToGrayscale(Bitmap bmp) {
    ArgumentNullException.ThrowIfNull(bmp);
    var w = bmp.Width; var h = bmp.Height;
    var data = new byte[w * h];
    var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
    try {
      unsafe {
        for (int y = 0; y < h; y++) {
          byte* row = (byte*)bd.Scan0 + y * bd.Stride;
          for (int x = 0; x < w; x++) {
            byte b = row[x * 3 + 0];
            byte g = row[x * 3 + 1];
            byte r = row[x * 3 + 2];
            data[y * w + x] = (byte)((r * 0.299) + (g * 0.587) + (b * 0.114));
          }
        }
      }
    }
    finally {
      bmp.UnlockBits(bd);
    }
    return new GrayImage(w, h, data);
    }
  }
}
