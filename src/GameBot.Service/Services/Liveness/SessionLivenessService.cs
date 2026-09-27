using System.Globalization;
using GameBot.Domain.Sessions;
using Microsoft.Extensions.Options;

namespace GameBot.Service.Services.Liveness;

/// <summary>
/// The default <see cref="ISessionLivenessService"/> (feature 106, research R-007).
/// </summary>
internal sealed class SessionLivenessService : ISessionLivenessService {
  private readonly IDeviceLivenessTracker _tracker;
  private readonly ISessionTransportCheck _transport;
  private readonly ISessionDirectCapture _directCapture;

  public SessionLivenessService(
    IDeviceLivenessTracker tracker,
    ISessionTransportCheck transport,
    ISessionDirectCapture directCapture,
    IOptions<DeviceLivenessOptions>? options = null) {
    _tracker = tracker;
    _transport = transport;
    _directCapture = directCapture;
    Options = (options?.Value ?? new DeviceLivenessOptions()).Normalized();
  }

  public DeviceLivenessOptions Options { get; }

  public DeviceLivenessReport Evaluate(EmulatorSession session) {
    ArgumentNullException.ThrowIfNull(session);
    var sample = _tracker.Sample(session.Id, HasDevice(session));
    return DeviceLivenessEvaluator.Evaluate(sample, Options, _tracker.Now);
  }

  public async Task<SessionLivenessProbeResult> ProbeAsync(EmulatorSession session, CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(session);
    if (!HasDevice(session)) {
      return new SessionLivenessProbeResult(null, Evaluate(session));
    }

    var serial = session.DeviceSerial!;
    var adb = await CheckTransportAsync(serial, ct).ConfigureAwait(false);
    var sample = _tracker.Sample(session.Id, hasDevice: true, transportReady: adb.Ok);
    var report = DeviceLivenessEvaluator.Evaluate(sample, Options, _tracker.Now);
    if (!report.NeedsProbe) {
      return new SessionLivenessProbeResult(adb, report);
    }

    // Rule 7: no capture data. One direct capture decides (research R-007).
    var captured = await TryDirectCaptureAsync(serial, ct).ConfigureAwait(false);
    var probed = captured
      ? report with { State = DeviceLivenessStates.Live, Reason = null, FrameAgeMs = 0, UnchangedMs = null, NeedsProbe = false }
      : report with { State = DeviceLivenessStates.NotLive, Reason = DeviceLivenessReasons.CaptureStalled, NeedsProbe = false };
    return new SessionLivenessProbeResult(adb, probed);
  }

  private static bool HasDevice(EmulatorSession session) => !string.IsNullOrWhiteSpace(session.DeviceSerial);

  /// <summary>The transport check with the limit <c>TransportCheckTimeoutMs</c>. A time-out gives <c>Ok = false</c>.</summary>
  private async Task<SessionTransportCheckResult> CheckTransportAsync(string serial, CancellationToken ct) {
    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
    limit.CancelAfter(Options.TransportCheckTimeoutMs);
    try {
      return await _transport.CheckAsync(serial, limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
      return new SessionTransportCheckResult(false, null, null, string.Format(
        CultureInfo.InvariantCulture, "adb get-state did not answer in {0} ms", Options.TransportCheckTimeoutMs));
    }
  }

  /// <summary>One direct capture with the limit <c>CaptureTimeoutMs</c>. A time-out or an error gives false.</summary>
  private async Task<bool> TryDirectCaptureAsync(string serial, CancellationToken ct) {
    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
    limit.CancelAfter(Options.CaptureTimeoutMs);
    try {
      return await _directCapture.TryCaptureAsync(serial, limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
      return false;
    }
  }
}
