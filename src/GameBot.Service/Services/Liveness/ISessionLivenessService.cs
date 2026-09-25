using GameBot.Domain.Sessions;

namespace GameBot.Service.Services.Liveness;

/// <summary>
/// The result of <see cref="ISessionLivenessService.ProbeAsync"/> (feature 106).
/// </summary>
/// <param name="Adb">The transport check. Null for a session with no device.</param>
/// <param name="Liveness">The liveness report.</param>
internal sealed record SessionLivenessProbeResult(SessionTransportCheckResult? Adb, DeviceLivenessReport Liveness);

/// <summary>
/// Tells if the device of a session is live (feature 106, data-model section 7). The inputs endpoint,
/// the capture headers and the queue use the data-only <see cref="Evaluate"/>. The health endpoint uses
/// the bounded <see cref="ProbeAsync"/>.
/// </summary>
internal interface ISessionLivenessService {
  /// <summary>The normalized liveness options.</summary>
  DeviceLivenessOptions Options { get; }

  /// <summary>The report from the tracker data only. No I/O.</summary>
  DeviceLivenessReport Evaluate(EmulatorSession session);

  /// <summary>
  /// The transport check (bounded), then the report, then one bounded direct capture when the report
  /// needs a probe. The worst time is <c>TransportCheckTimeoutMs + CaptureTimeoutMs</c>.
  /// </summary>
  Task<SessionLivenessProbeResult> ProbeAsync(EmulatorSession session, CancellationToken ct);
}
