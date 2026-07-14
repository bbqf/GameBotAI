namespace GameBot.Domain.Commands.SelfReschedule;

/// <summary>
/// Typed view of the optional <c>ocrOffset</c> spec on a <c>reschedule-self</c> Timer payload
/// (feature 068). Describes how to derive the Timer relative offset at runtime by OCR-reading an
/// on-screen countdown timer, with a required static <see cref="Fallback"/> used on any failure and
/// plausibility bounds (<see cref="Min"/>/<see cref="Max"/>) that reject implausible reads.
/// </summary>
public sealed class SelfRescheduleOcrOffset {
  /// <summary>Screen region to read, in the captured-screen pixel space used by image detection.</summary>
  public required OcrOffsetRegion Region { get; init; }

  /// <summary>Required static offset used when OCR fails, is empty, or is out of bounds.</summary>
  public required System.TimeSpan Fallback { get; init; }

  /// <summary>Lower plausibility bound; a parsed value below this falls back (default 00:00:01).</summary>
  public required System.TimeSpan Min { get; init; }

  /// <summary>Upper plausibility bound; a parsed value above this falls back (default 24:00:00).</summary>
  public required System.TimeSpan Max { get; init; }
}

/// <summary>Captured-screen pixel rectangle for an <see cref="SelfRescheduleOcrOffset"/> read.</summary>
public readonly record struct OcrOffsetRegion(int X, int Y, int Width, int Height);
