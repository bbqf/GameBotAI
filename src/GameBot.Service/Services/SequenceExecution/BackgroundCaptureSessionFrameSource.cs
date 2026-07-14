using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using GameBot.Emulator.Session;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>
/// <see cref="ISessionFrameSource"/> backed by the <see cref="BackgroundScreenCaptureService"/>
/// cache (feature 068). Decodes the immutable PNG snapshot rather than cloning the cached Bitmap,
/// which the capture loop disposes when it swaps in a new frame.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class BackgroundCaptureSessionFrameSource : ISessionFrameSource {
  private readonly BackgroundScreenCaptureService _captureService;

  public BackgroundCaptureSessionFrameSource(BackgroundScreenCaptureService captureService) {
    _captureService = captureService;
  }

  public Bitmap? Capture(string sessionId) {
    if (string.IsNullOrWhiteSpace(sessionId)) {
      return null;
    }

    var frame = _captureService.GetCachedFrame(sessionId);
    if (frame is null) {
      return null;
    }

    using var ms = new MemoryStream(frame.PngBytes, writable: false);
    using var tmp = new Bitmap(ms);
    return new Bitmap(tmp); // detach from the stream so the caller can dispose independently
  }
}
