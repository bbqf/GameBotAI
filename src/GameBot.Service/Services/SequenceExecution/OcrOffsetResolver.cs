using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>
/// Real <see cref="IOcrOffsetResolver"/> (feature 068): captures the session frame, crops the
/// configured region, OCR-reads it, parses a duration, and bounds-checks it. Any failure — no
/// session, no capture, region off-frame, OCR error/empty, unparseable, or out of bounds — yields
/// the spec's static fallback with a reason (FR-005/FR-006); it never throws.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OcrOffsetResolver : IOcrOffsetResolver {
  private readonly ISessionFrameSource _frameSource;
  private readonly ITextOcr _ocr;

  public OcrOffsetResolver(ISessionFrameSource frameSource, ITextOcr ocr) {
    _frameSource = frameSource;
    _ocr = ocr;
  }

  public OcrOffsetResolution Resolve(string? sessionId, SelfRescheduleOcrOffset spec) {
    ArgumentNullException.ThrowIfNull(spec);

    if (string.IsNullOrWhiteSpace(sessionId)) {
      return Fallback(spec, "no-session", null);
    }

    Bitmap? frame = null;
    Bitmap? cropped = null;
    try {
      frame = _frameSource.Capture(sessionId);
      if (frame is null) {
        return Fallback(spec, "no-capture", null);
      }

      cropped = CropRegion(frame, spec.Region);
      if (cropped is null) {
        return Fallback(spec, "region-invalid", null);
      }

      var text = _ocr.Recognize(cropped).Text;
      if (string.IsNullOrWhiteSpace(text)) {
        return Fallback(spec, "ocr-empty", text ?? string.Empty);
      }

      if (!CooldownDurationParser.TryParse(text, out var parsed)) {
        return Fallback(spec, "parse-failed", text);
      }

      if (parsed < spec.Min || parsed > spec.Max) {
        return Fallback(spec, "out-of-bounds", text);
      }

      return new OcrOffsetResolution(parsed, OcrOffsetSource.Ocr, text, null);
    }
    catch (Exception) {
      // FR-005: OCR/capture faults must never fail the step — always reschedule via fallback.
      return Fallback(spec, "ocr-error", null);
    }
    finally {
      cropped?.Dispose();
      frame?.Dispose();
    }
  }

  private static OcrOffsetResolution Fallback(SelfRescheduleOcrOffset spec, string reason, string? text) =>
    new(spec.Fallback, OcrOffsetSource.Fallback, text, reason);

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
