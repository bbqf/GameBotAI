namespace GameBot.Emulator.Adb;

/// <summary>
/// The ADB calls that <see cref="Session.SessionManager"/> makes for a bound device (feature 106). This
/// internal seam lets unit tests simulate a hung tap, a failed tap and a hung screenshot with no
/// <c>adb</c> executable. <see cref="AdbClient"/> is the production implementation.
/// </summary>
internal interface IAdbSessionClient {
  /// <summary>Sends <c>input tap</c>.</summary>
  Task<(int ExitCode, string StdOut, string StdErr)> TapAsync(int x, int y, CancellationToken ct = default);

  /// <summary>Sends <c>input swipe</c>.</summary>
  Task<(int ExitCode, string StdOut, string StdErr)> SwipeAsync(int x1, int y1, int x2, int y2, int? durationMs = null, CancellationToken ct = default);

  /// <summary>Sends <c>input keyevent</c>.</summary>
  Task<(int ExitCode, string StdOut, string StdErr)> KeyEventAsync(int keyCode, CancellationToken ct = default);

  /// <summary>Captures one PNG screenshot.</summary>
  Task<byte[]> GetScreenshotPngAsync(CancellationToken ct = default);
}
