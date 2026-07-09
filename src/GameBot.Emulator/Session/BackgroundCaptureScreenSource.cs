using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Emulator.Session;

/// <summary>
/// IScreenSource implementation backed by the BackgroundScreenCaptureService cache.
/// Returns a clone of the latest cached Bitmap for the first running session.
/// Does not call ADB directly — all captures come from the background loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BackgroundCaptureScreenSource : IScreenSource {
  private readonly BackgroundScreenCaptureService _captureService;
  private readonly ISessionManager _sessions;

  public BackgroundCaptureScreenSource(BackgroundScreenCaptureService captureService, ISessionManager sessions) {
    _captureService = captureService;
    _sessions = sessions;
  }

  public Bitmap? GetLatestScreenshot() {
    var sess = _sessions.ListSessions()
        .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.DeviceSerial) && s.Status == Domain.Sessions.SessionStatus.Running);
    if (sess is null) return null;

    var frame = _captureService.GetCachedFrame(sess.Id);
    if (frame is null) return null;

    // Decode from the immutable PNG snapshot instead of cloning frame.Bitmap:
    // the capture loop disposes the cached Bitmap when it swaps in a new frame,
    // and reading a GDI+ Bitmap concurrently with its disposal throws.
    using var ms = new MemoryStream(frame.PngBytes, writable: false);
    using var tmp = new Bitmap(ms);
    return new Bitmap(tmp); // detach from stream so the caller can dispose it independently
  }
}
