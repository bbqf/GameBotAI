using GameBot.Domain.Commands.SelfReschedule;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>
/// <see cref="IOcrOffsetResolver"/> used when screen capture / OCR are unavailable (non-Windows or
/// ADB-disabled test hosts): always returns the spec's static fallback so a self-rescheduling
/// sequence still reschedules (FR-005) instead of failing DI graph construction.
/// </summary>
internal sealed class StaticFallbackOcrOffsetResolver : IOcrOffsetResolver {
  public OcrOffsetResolution Resolve(string? sessionId, SelfRescheduleOcrOffset spec) {
    System.ArgumentNullException.ThrowIfNull(spec);
    return new OcrOffsetResolution(spec.Fallback, OcrOffsetSource.Fallback, null, "ocr-unavailable");
  }
}
