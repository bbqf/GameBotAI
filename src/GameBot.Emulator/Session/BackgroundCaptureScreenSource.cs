using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Emulator.Session;

/// <summary>
/// Session-agnostic <see cref="IScreenSource"/> for consumers that cannot name the session they are
/// acting for (trigger evaluators, condition adapters, the standalone trigger worker).
/// </summary>
/// <remarks>
/// Resolution order (feature 079):
/// <list type="number">
///   <item>the ambient <see cref="DeviceContext"/> pushed by the queue run this call is executing
///         inside — so concurrent runs observe their own devices;</item>
///   <item>the single running session, when exactly one exists — preserving single-emulator
///         behaviour unchanged;</item>
///   <item><c>null</c> when several sessions are running and none is ambient, rather than picking one
///         arbitrarily.</item>
/// </list>
/// Before this feature the source always returned the frame of the <i>first</i> running session it
/// found, which silently gave one queue run another run's screen.
/// Does not call ADB directly — all captures come from the background loop.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class BackgroundCaptureScreenSource : IScreenSource {
  private readonly BackgroundScreenCaptureService _captureService;
  private readonly ISessionManager _sessions;
  private readonly IDeviceContextAccessor? _deviceContext;

  /// <summary>Creates the source.</summary>
  /// <param name="captureService">The per-session background capture cache.</param>
  /// <param name="sessions">Session manager, used for the single-running-session fallback.</param>
  /// <param name="deviceContext">
  /// Ambient device context. When null (tests that omit it) only the single-session fallback applies.
  /// </param>
  public BackgroundCaptureScreenSource(
      BackgroundScreenCaptureService captureService,
      ISessionManager sessions,
      IDeviceContextAccessor? deviceContext = null) {
    _captureService = captureService;
    _sessions = sessions;
    _deviceContext = deviceContext;
  }

  /// <inheritdoc />
  public Bitmap? GetLatestScreenshot() {
    var sessionId = ResolveSessionId();
    return sessionId is null ? null : SessionScopedScreenSource.DecodeCachedFrame(_captureService, sessionId);
  }

  /// <summary>
  /// Resolves the session to observe, or <c>null</c> when it is not unambiguously determined.
  /// </summary>
  private string? ResolveSessionId() {
    // 1. The run whose execution flow we are on wins outright.
    if (_deviceContext?.Current is { } ambient) {
      return ambient.SessionId;
    }

    // 2. Exactly one running session: unambiguous, so keep the pre-079 single-emulator behaviour.
    //    3. More than one (or none): ambiguous, so observe nothing rather than the wrong device.
    var running = _sessions.ListSessions()
      .Where(s => !string.IsNullOrWhiteSpace(s.DeviceSerial) && s.Status == Domain.Sessions.SessionStatus.Running)
      .ToList();
    return running.Count == 1 ? running[0].Id : null;
  }
}
