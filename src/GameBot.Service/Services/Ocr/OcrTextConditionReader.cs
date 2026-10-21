using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Services.SequenceExecution;

namespace GameBot.Service.Services.Ocr;

/// <summary>
/// Reads the region of an <c>ocrText</c> condition from the screen of a session (issue #338). The interface has
/// no platform attribute, so the run service can use it on every platform.
/// </summary>
internal interface IOcrTextConditionReader {
  /// <summary>
  /// Reads the region. The method does not throw for a capture or engine fault. It returns one reason code from
  /// <see cref="OcrTextFailureReasons"/> for each failure.
  /// </summary>
  Task<OcrTextReadResult> ReadAsync(OcrTextStepCondition condition, string sessionId, CancellationToken ct);
}

/// <summary>
/// The real reader: one frame from <see cref="ISessionFrameSource"/>, then <see cref="OcrRegionReader"/>, then
/// <see cref="OcrTextParsers"/>. These are the same calls that <c>POST /api/ocr/read</c> uses, so the same frame
/// and region give the same raw text. The reader sends no input to the emulator.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OcrTextConditionReader : IOcrTextConditionReader {
  private readonly ISessionFrameSource? _frames;
  private readonly ITextOcr? _ocr;

  public OcrTextConditionReader(ISessionFrameSource? frames = null, ITextOcr? ocr = null) {
    _frames = frames;
    _ocr = ocr;
  }

  /// <summary>Test seam: replaces the shared crop and read code. Production code leaves it null.</summary>
  internal Func<Bitmap, OcrOffsetRegion, ITextOcr, OcrRegionReadResult>? ReaderOverride { get; set; }

  public Task<OcrTextReadResult> ReadAsync(OcrTextStepCondition condition, string sessionId, CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(condition);
    ct.ThrowIfCancellationRequested();
    return Task.FromResult(Read(condition, sessionId));
  }

  private OcrTextReadResult Read(OcrTextStepCondition condition, string sessionId) {
    var region = condition.Region;
    if (region is null || region.Width <= 0 || region.Height <= 0) {
      return Fail(OcrTextFailureReasons.RegionInvalid);
    }

    Bitmap? frame;
    try {
      frame = _frames?.Capture(sessionId);
    }
    catch (Exception) {
      frame = null;
    }
    if (frame is null) {
      return Fail(OcrTextFailureReasons.CaptureFailed);
    }

    using (frame) {
      return ReadFrame(frame, region, condition.Parser);
    }
  }

  private OcrTextReadResult ReadFrame(Bitmap frame, PixelRegion region, string? parser) {
    if (_ocr is null) {
      return Fail(OcrTextFailureReasons.OcrUnavailable);
    }

    var inside = region.X >= 0
      && region.Y >= 0
      && (long)region.X + region.Width <= frame.Width
      && (long)region.Y + region.Height <= frame.Height;
    if (!inside) {
      return Fail(OcrTextFailureReasons.RegionOutsideFrame);
    }

    OcrRegionReadResult read;
    try {
      var offsetRegion = new OcrOffsetRegion(region.X, region.Y, region.Width, region.Height);
      read = (ReaderOverride ?? OcrRegionReader.Read)(frame, offsetRegion, _ocr);
    }
    catch (Exception) {
      return Fail(OcrTextFailureReasons.OcrError);
    }
    if (read.CropFailed) {
      return Fail(OcrTextFailureReasons.CaptureFailed);
    }

    var text = read.Text ?? string.Empty;
    if (string.IsNullOrWhiteSpace(parser)) {
      return new OcrTextReadResult(text, null, null);
    }

    // An unknown parser name (for example from a gamepack import) gives parse-failed at run time.
    if (!OcrTextParsers.TryGetName(parser, out var canonical)
        || !OcrTextParsers.TryParse(canonical, text, out var value, out _)) {
      return new OcrTextReadResult(text, null, OcrTextFailureReasons.ParseFailed);
    }

    return new OcrTextReadResult(text, value, null);
  }

  private static OcrTextReadResult Fail(string reason) => new(null, null, reason);
}
