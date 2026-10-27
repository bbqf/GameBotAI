using System;
using System.Runtime.Versioning;

namespace GameBot.Emulator.Session;

/// <summary>
/// Stops the capture loop of a session when the session manager removes the session (feature 166,
/// FR-013). Thus no capture loop stays on for a session that does not exist, also after the idle sweep.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SessionCaptureCleanup {
  /// <summary>
  /// Connects <paramref name="captureService"/> to the removal event of <paramref name="sessionManager"/>.
  /// When the session manager does not implement <see cref="ISessionRemovalNotifier"/>, this method does
  /// nothing. Call it one time for each capture service.
  /// </summary>
  /// <param name="sessionManager">The session manager that removes the sessions.</param>
  /// <param name="captureService">The capture service that owns the capture loops.</param>
  public static void Attach(ISessionManager sessionManager, BackgroundScreenCaptureService captureService) {
    ArgumentNullException.ThrowIfNull(captureService);
    if (sessionManager is not ISessionRemovalNotifier notifier) return;
    // A handler exception goes to the session manager, which writes a warning log and continues.
    notifier.SessionRemoved += (_, e) => captureService.StopCapture(e.SessionId);
  }
}
