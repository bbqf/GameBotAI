using System;

namespace GameBot.Domain.Sessions;

/// <summary>
/// Identifies the emulator device an execution flow is acting on (feature 079).
/// </summary>
/// <remarks>
/// Carried ambiently by <see cref="IDeviceContextAccessor"/> so screen observation performed deep
/// inside a queue run — image/text evaluators and condition adapters that take no session parameter —
/// resolves against that run's own device instead of "whichever session happens to be first".
/// Immutable and therefore safe to share across threads.
/// </remarks>
/// <param name="SessionId">The emulator session id the flow holds. Never blank.</param>
/// <param name="DeviceSerial">The ADB serial when known; diagnostics only, the session id is the key.</param>
public sealed record DeviceContext(string SessionId, string? DeviceSerial = null) {
  /// <summary>Creates a context, rejecting a blank session id.</summary>
  /// <exception cref="ArgumentException">The session id is null, empty or whitespace.</exception>
  public static DeviceContext For(string sessionId, string? deviceSerial = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    return new DeviceContext(sessionId, string.IsNullOrWhiteSpace(deviceSerial) ? null : deviceSerial);
  }
}
