using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Service.Services.Ocr;

/// <summary>
/// Result of <see cref="OcrRegionReader.Read"/>. When <see cref="CropFailed"/> is true, the crop gave no image
/// and the engine did not run.
/// </summary>
internal readonly record struct OcrRegionReadResult(bool CropFailed, string? Text, double Confidence);

/// <summary>
/// Shared crop and read code. The <c>ocrOffset</c> step (<c>OcrOffsetResolver</c>) and the
/// <c>POST /api/ocr/read</c> endpoint both call it, so both read the same text for the same frame and region.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class OcrRegionReader {
  /// <summary>
  /// Crops the region from the frame, then runs the engine on the crop. A crop that gives no image gives a result
  /// with <see cref="OcrRegionReadResult.CropFailed"/> set. An engine fault is not caught here.
  /// </summary>
  public static OcrRegionReadResult Read(Bitmap frame, OcrOffsetRegion region, ITextOcr ocr) {
    ArgumentNullException.ThrowIfNull(frame);
    ArgumentNullException.ThrowIfNull(ocr);

    using var cropped = CropRegion(frame, region);
    if (cropped is null) {
      return new OcrRegionReadResult(true, null, 0);
    }

    var result = ocr.Recognize(cropped);
    return new OcrRegionReadResult(false, result.Text, result.Confidence);
  }

  // Crops the region in absolute captured-screen pixel space (FR-002). Clamps to the frame; returns
  // null when the region starts entirely outside the frame or has non-positive size.
  private static Bitmap? CropRegion(Bitmap frame, OcrOffsetRegion region) {
    if (region.Width <= 0 || region.Height <= 0) {
      return null;
    }
    if (region.X >= frame.Width || region.Y >= frame.Height) {
      return null;
    }

    var rx = Math.Clamp(region.X, 0, frame.Width - 1);
    var ry = Math.Clamp(region.Y, 0, frame.Height - 1);
    var rw = Math.Clamp(region.Width, 1, frame.Width - rx);
    var rh = Math.Clamp(region.Height, 1, frame.Height - ry);

    try {
      var dest = new Bitmap(rw, rh, PixelFormat.Format24bppRgb);
      using var g = Graphics.FromImage(dest);
      g.DrawImage(frame, new Rectangle(0, 0, rw, rh), new Rectangle(rx, ry, rw, rh), GraphicsUnit.Pixel);
      return dest;
    }
    catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) {
      return null;
    }
  }
}
