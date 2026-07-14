using GameBot.Domain.Commands.SelfReschedule;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>Offset-source labels recorded in the execution log (feature 068, FR-007).</summary>
internal static class OcrOffsetSource {
  public const string Ocr = "ocr";
  public const string Fallback = "fallback";
}

/// <summary>
/// Outcome of resolving a <c>reschedule-self</c> Timer offset from an on-screen countdown
/// (feature 068). <see cref="EffectiveOffset"/> is always usable — on any failure it is the
/// spec's static fallback and <see cref="Source"/> is <see cref="OcrOffsetSource.Fallback"/>.
/// </summary>
/// <param name="EffectiveOffset">The offset the reschedule should use.</param>
/// <param name="Source">Either <c>ocr</c> or <c>fallback</c>.</param>
/// <param name="RecognizedText">The OCR text read, when a capture/OCR was attempted.</param>
/// <param name="Reason">On the fallback path, why the OCR value was not used.</param>
internal sealed record OcrOffsetResolution(
  System.TimeSpan EffectiveOffset,
  string Source,
  string? RecognizedText,
  string? Reason);

/// <summary>
/// Resolves an <see cref="SelfRescheduleOcrOffset"/> to an effective Timer offset by capturing the
/// session frame, cropping the region, OCR-reading it, parsing a duration, and bounds-checking it.
/// Never throws — every failure path yields the static fallback (FR-005).
/// </summary>
internal interface IOcrOffsetResolver {
  OcrOffsetResolution Resolve(string? sessionId, SelfRescheduleOcrOffset spec);
}
