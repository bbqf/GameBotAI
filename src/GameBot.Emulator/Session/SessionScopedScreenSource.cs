using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Emulator.Session;

/// <summary>
/// <see cref="IScreenSource"/> bound to exactly one emulator session (feature 079).
/// </summary>
/// <remarks>
/// Returns the latest cached frame for that session and nothing else, so two concurrent queue runs can
/// never observe one another's screen. Returns <c>null</c> when the session has no cached frame yet or
/// its capture loop has stopped; it never substitutes another session's frame.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SessionScopedScreenSource : IScreenSource {
  private readonly BackgroundScreenCaptureService _captureService;
  private readonly string _sessionId;

  /// <summary>Creates a source observing <paramref name="sessionId"/> for its whole lifetime.</summary>
  /// <param name="captureService">The per-session background capture cache.</param>
  /// <param name="sessionId">The session to observe. Must not be blank.</param>
  public SessionScopedScreenSource(BackgroundScreenCaptureService captureService, string sessionId) {
    ArgumentNullException.ThrowIfNull(captureService);
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    _captureService = captureService;
    _sessionId = sessionId;
  }

  /// <summary>The session this source is bound to.</summary>
  public string SessionId => _sessionId;

  /// <inheritdoc />
  public Bitmap? GetLatestScreenshot() => DecodeCachedFrame(_captureService, _sessionId);

  /// <summary>
  /// Decodes the session's latest cached frame into a detached <see cref="Bitmap"/>, or <c>null</c>
  /// when no frame is cached.
  /// </summary>
  /// <remarks>
  /// Decoding from the frame's immutable PNG bytes rather than cloning <c>frame.Bitmap</c> is
  /// deliberate: the capture loop disposes the cached bitmap when it swaps in a new frame, and reading
  /// a GDI+ bitmap concurrently with its disposal throws.
  /// </remarks>
  internal static Bitmap? DecodeCachedFrame(BackgroundScreenCaptureService captureService, string sessionId) {
    var frame = captureService.GetCachedFrame(sessionId);
    if (frame is null) return null;

    using var ms = new MemoryStream(frame.PngBytes, writable: false);
    using var tmp = new Bitmap(ms);
    return new Bitmap(tmp); // detach from the stream so the caller can dispose it independently
  }
}
