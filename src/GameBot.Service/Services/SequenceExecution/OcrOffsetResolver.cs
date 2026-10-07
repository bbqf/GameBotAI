using System;
using System.Drawing;
using System.Runtime.Versioning;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Services.Ocr;

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
    try {
      frame = _frameSource.Capture(sessionId);
      if (frame is null) {
        return Fallback(spec, "no-capture", null);
      }

      var read = OcrRegionReader.Read(frame, spec.Region, _ocr);
      if (read.CropFailed) {
        return Fallback(spec, "region-invalid", null);
      }

      var text = read.Text;
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
      frame?.Dispose();
    }
  }

  private static OcrOffsetResolution Fallback(SelfRescheduleOcrOffset spec, string reason, string? text) =>
    new(spec.Fallback, OcrOffsetSource.Fallback, text, reason);
}
