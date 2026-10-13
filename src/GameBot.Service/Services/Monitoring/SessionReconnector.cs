using GameBot.Emulator.Session;

namespace GameBot.Service.Services.Monitoring;

/// <summary>
/// Feature 141 (FR-015): connects a session to its emulator again after a restart. The session id does not
/// change, so the selection in the web UI and the queue owner link stay valid.
/// </summary>
internal interface ISessionReconnector {
  /// <summary>Stops the capture loop of the session and starts it again for the same session id and serial.</summary>
  Task ReconnectAsync(string sessionId, string deviceSerial, CancellationToken ct = default);
}

/// <summary>
/// The real reconnector. It calls <c>StopCapture</c> and then <c>StartCapture</c>. When the host has no
/// capture service (non-Windows host, or ADB is off), it does nothing.
/// </summary>
internal sealed class SessionReconnector : ISessionReconnector {
  private readonly BackgroundScreenCaptureService? _capture;

  public SessionReconnector(BackgroundScreenCaptureService? capture = null) {
    _capture = capture;
  }

  public Task ReconnectAsync(string sessionId, string deviceSerial, CancellationToken ct = default) {
    ct.ThrowIfCancellationRequested();
    if (_capture is null) return Task.CompletedTask;
    _capture.StopCapture(sessionId);
    _capture.StartCapture(sessionId, deviceSerial);
    return Task.CompletedTask;
  }
}
