using System.Drawing;
using System.Runtime.Versioning;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Services.Ocr;
using GameBot.Service.Services.SequenceExecution;

namespace GameBot.Service.Services.Quiz;

/// <summary>
/// Reads the text of screen regions for the quiz steps (feature 142, research R2). One call captures one frame for all
/// regions of one step.
/// </summary>
internal interface IQuizScreenReader {
  /// <summary>
  /// Reads the text of each region from one frame of the session. Returns null when no frame can be read (no
  /// session, no frame, or an OCR fault). An entry of the list is null when its region cannot be cropped.
  /// </summary>
  IReadOnlyList<string?>? ReadRegions(string? sessionId, IReadOnlyList<OcrOffsetRegion> regions);
}

/// <summary>The reader for a host with no screen capture or no OCR: every read fails.</summary>
internal sealed class NullQuizScreenReader : IQuizScreenReader {
  public IReadOnlyList<string?>? ReadRegions(string? sessionId, IReadOnlyList<OcrOffsetRegion> regions) => null;
}

/// <summary>
/// The real reader: it uses <see cref="ISessionFrameSource"/> for one frame and <see cref="OcrRegionReader"/> with the
/// injected <see cref="ITextOcr"/>, the same code that <c>ocrOffset</c> and <c>POST /api/ocr/read</c> use.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class QuizScreenReader : IQuizScreenReader {
  private readonly ISessionFrameSource _frames;
  private readonly ITextOcr _ocr;

  public QuizScreenReader(ISessionFrameSource frames, ITextOcr ocr) {
    _frames = frames;
    _ocr = ocr;
  }

  public IReadOnlyList<string?>? ReadRegions(string? sessionId, IReadOnlyList<OcrOffsetRegion> regions) {
    ArgumentNullException.ThrowIfNull(regions);
    if (string.IsNullOrWhiteSpace(sessionId)) {
      return null;
    }

    Bitmap? frame = null;
    try {
      frame = _frames.Capture(sessionId);
      if (frame is null) {
        return null;
      }

      var texts = new List<string?>(regions.Count);
      foreach (var region in regions) {
        var read = OcrRegionReader.Read(frame, region, _ocr);
        texts.Add(read.CropFailed ? null : read.Text);
      }

      return texts;
    }
    catch (Exception) {
      // An OCR or capture fault is not a step failure. The lookup gives the outcome unreadable.
      return null;
    }
    finally {
      frame?.Dispose();
    }
  }
}
