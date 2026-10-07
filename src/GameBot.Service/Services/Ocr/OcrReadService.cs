using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Emulator.Session;
using GameBot.Service.Models;
using GameBot.Service.Services.SequenceExecution;

namespace GameBot.Service.Services.Ocr;

/// <summary>
/// Result of <see cref="OcrReadService.Read"/>: an answer, or an error with a status code and an error code.
/// The endpoint maps it to HTTP.
/// </summary>
internal sealed record OcrReadOutcome(OcrReadResponse? Response, int Status, string? Code, string? Message) {
  internal static OcrReadOutcome Ok(OcrReadResponse response) => new(response, 200, null, null);
  internal static OcrReadOutcome Fail(int status, string code, string message) => new(null, status, code, message);
}

/// <summary>
/// Runs <c>POST /api/ocr/read</c>. Checks run in a fixed order and the first fault ends the request:
/// (1) checks that need no lookup, (2) lookup of the serial or capture id, (3) capture or decode,
/// (4) engine availability, (5) region inside the frame, (6) crop and read, (7) parse.
/// The service sends no input to the emulator and keeps no cache.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OcrReadService {
  internal const string SourceSerial = "serial";
  internal const string SourceCaptureId = "captureId";

  private readonly ISessionManager? _sessions;
  private readonly CaptureSessionStore _captures;
  private readonly ISessionFrameSource? _frames;
  private readonly ITextOcr? _ocr;

  public OcrReadService(
      CaptureSessionStore captures,
      ISessionManager? sessions = null,
      ISessionFrameSource? frames = null,
      ITextOcr? ocr = null) {
    _captures = captures;
    _sessions = sessions;
    _frames = frames;
    _ocr = ocr;
  }

  /// <summary>Test seam: replaces the shared reader. Production code leaves it null.</summary>
  internal Func<Bitmap, OcrOffsetRegion, ITextOcr, OcrRegionReadResult>? ReaderOverride { get; set; }

  /// <summary>Runs the request. This method does not throw for an input error or a host fault.</summary>
  public OcrReadOutcome Read(OcrReadRequest? request) {
    var invalid = ValidateRequest(request, out var source, out var parser);
    if (invalid is not null) {
      return invalid;
    }
    var req = request!;
    var region = req.Region!;

    return source == SourceSerial
      ? ReadFromSerial(req.Serial!.Trim(), region, parser)
      : ReadFromCapture(req.CaptureId!.Trim(), region, parser);
  }

  // Step 1: all checks that need no lookup.
  private static OcrReadOutcome? ValidateRequest(OcrReadRequest? request, out string source, out string? parser) {
    source = string.Empty;
    parser = null;
    if (request is null) {
      return Fail(400, "invalid_request", "The request body is missing. Send a JSON body with a source and a region.");
    }

    var hasSerial = !string.IsNullOrWhiteSpace(request.Serial);
    var hasCapture = !string.IsNullOrWhiteSpace(request.CaptureId);
    if (hasSerial == hasCapture) {
      return Fail(400, "invalid_request", "Set exactly one of serial and captureId.");
    }
    if (request.Region is null) {
      return Fail(400, "invalid_request", "The region is required. Send x, y, width and height.");
    }
    if (!string.IsNullOrWhiteSpace(request.Parser)) {
      if (!OcrTextParsers.TryGetName(request.Parser, out var canonical)) {
        return Fail(400, "unknown_parser",
          "The parser is not known. Supported parsers: " + string.Join(", ", OcrTextParsers.Names) + ".");
      }
      parser = canonical;
    }
    if (request.Region.Width <= 0 || request.Region.Height <= 0) {
      return Fail(400, "invalid_region", "The region width and height must be more than zero.");
    }

    source = hasSerial ? SourceSerial : SourceCaptureId;
    return null;
  }

  private OcrReadOutcome ReadFromSerial(string serial, OcrRegionDto region, string? parser) {
    // Step 2: the first running session with this serial.
    var session = _sessions?.ListSessions().FirstOrDefault(s =>
      s.Status == SessionStatus.Running && string.Equals(s.DeviceSerial, serial, StringComparison.Ordinal));
    if (session is null) {
      return Fail(404, "serial_not_found", "No running session has this serial. Start a session for the device and try again.");
    }

    // Step 3: capture the frame.
    if (_frames is null) {
      return Fail(503, "capture_unavailable", "This host has no capture service.");
    }
    Bitmap? frame;
    try {
      frame = _frames.Capture(session.Id);
    }
    catch (Exception) {
      frame = null;
    }
    if (frame is null) {
      return Fail(502, "capture_failed", "The service has no frame for this session. Try again.");
    }

    using (frame) {
      return ReadFrame(frame, SourceSerial, region, parser);
    }
  }

  private OcrReadOutcome ReadFromCapture(string captureId, OcrRegionDto region, string? parser) {
    // Step 2: the stored capture.
    if (!_captures.TryGet(captureId, out var capture) || capture is null) {
      return Fail(404, "capture_not_found", "The capture id is not known or it has expired.");
    }

    // Step 3: decode the stored picture. The stream stays open while the bitmap is in use.
    using var stream = new MemoryStream(capture.Png, writable: false);
    Bitmap? frame = null;
    try {
      try {
        frame = new Bitmap(stream);
      }
      catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) {
        return Fail(502, "capture_failed", "The stored capture does not decode as a picture.");
      }
      return ReadFrame(frame, SourceCaptureId, region, parser);
    }
    finally {
      frame?.Dispose();
    }
  }

  // Steps 4 to 7. Both sources use this method, so the checks are the same for both.
  private OcrReadOutcome ReadFrame(Bitmap frame, string source, OcrRegionDto region, string? parser) {
    // Step 4: engine availability.
    if (_ocr is null) {
      return Fail(503, "ocr_unavailable", "No OCR engine is available on this host.");
    }

    // Step 5: region inside the frame (one shared check).
    var outside = CheckRegionInsideFrame(region, frame.Width, frame.Height);
    if (outside is not null) {
      return outside;
    }

    // Step 6: crop and read.
    OcrRegionReadResult read;
    try {
      var offsetRegion = new OcrOffsetRegion(region.X, region.Y, region.Width, region.Height);
      read = (ReaderOverride ?? OcrRegionReader.Read)(frame, offsetRegion, _ocr);
    }
    catch (Exception) {
      return Fail(503, "ocr_unavailable", "The OCR engine failed to read the region.");
    }
    if (read.CropFailed) {
      return Fail(502, "capture_failed", "The service could not crop the region from the frame.");
    }

    var response = new OcrReadResponse {
      Text = read.Text ?? string.Empty,
      Confidence = read.Confidence,
      Source = source,
      FrameWidth = frame.Width,
      FrameHeight = frame.Height
    };

    // Step 7: parse. A parse failure is not an error.
    if (parser is not null) {
      response.Parser = parser;
      if (OcrTextParsers.TryParse(parser, response.Text, out var value, out var totalSeconds)) {
        response.Parsed = new OcrParsedValue { Value = value, TotalSeconds = totalSeconds };
      }
      else {
        response.ParseFailureReason = "text did not parse as " + parser;
      }
    }
    return OcrReadOutcome.Ok(response);
  }

  private static OcrReadOutcome? CheckRegionInsideFrame(OcrRegionDto region, int frameWidth, int frameHeight) {
    var inside = region.X >= 0
      && region.Y >= 0
      && (long)region.X + region.Width <= frameWidth
      && (long)region.Y + region.Height <= frameHeight;
    return inside
      ? null
      : Fail(400, "invalid_region",
          $"The region is not fully inside the frame. The frame is {frameWidth} x {frameHeight} pixels.");
  }

  private static OcrReadOutcome Fail(int status, string code, string message) =>
    OcrReadOutcome.Fail(status, code, message);
}
