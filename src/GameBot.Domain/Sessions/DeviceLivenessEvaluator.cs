namespace GameBot.Domain.Sessions;

/// <summary>
/// Calculates the liveness of a device from one <see cref="DeviceLivenessSample"/> (feature 106,
/// research R-002). This class is pure: it has no I/O and no clock. The caller gives the time.
/// <para>
/// The rules apply in this order. The first rule that applies gives the state and the reason:
/// </para>
/// <list type="number">
/// <item>No device: <c>unknown</c>.</item>
/// <item>The transport is not ready: <c>not_live</c>, <c>transport_not_ready</c>.</item>
/// <item>The last input timed out, or it is pending for longer than the input time limit, and no
/// frame change came after its start: <c>not_live</c>, <c>input_timeout</c>.</item>
/// <item>A capture loop runs and no capture completed for longer than the capture-stall limit:
/// <c>not_live</c>, <c>capture_stalled</c>.</item>
/// <item>A capture loop runs, the frame did not change for longer than the stale limit, and the first
/// input after the last change is older than the stale limit: <c>not_live</c>,
/// <c>no_change_after_input</c>.</item>
/// <item>A capture loop runs and has a completed capture: <c>live</c>.</item>
/// <item>All other cases: <c>unknown</c>, and the caller must do a probe.</item>
/// </list>
/// </summary>
public static class DeviceLivenessEvaluator {
  /// <summary>Calculates the liveness report of <paramref name="sample"/> at <paramref name="now"/>.</summary>
  public static DeviceLivenessReport Evaluate(DeviceLivenessSample sample, DeviceLivenessOptions options, DateTimeOffset now) {
    ArgumentNullException.ThrowIfNull(sample);
    ArgumentNullException.ThrowIfNull(options);

    var frameAgeMs = Since(sample.LastCaptureAt, now);
    var unchangedMs = sample.LastCaptureAt is null ? null : Since(sample.LastChangeAt ?? sample.LastCaptureAt, now);
    var stale = sample.CaptureLoopRunning
      && (unchangedMs > options.StaleLimitMs || frameAgeMs > options.CaptureStallLimitMs);
    var outcome = sample.LastInputOutcome is { } o ? InputOutcomes.ToWire(o) : null;

    DeviceLivenessReport Report(string state, string? reason, bool needsProbe = false) =>
      new(state, reason, frameAgeMs, unchangedMs, stale, sample.LastInputAt, outcome, needsProbe);

    if (!sample.HasDevice) {
      return Report(DeviceLivenessStates.Unknown, null);
    }
    if (sample.TransportReady == false) {
      return Report(DeviceLivenessStates.NotLive, DeviceLivenessReasons.TransportNotReady);
    }
    if (InputTimedOut(sample, options, now)) {
      return Report(DeviceLivenessStates.NotLive, DeviceLivenessReasons.InputTimeout);
    }
    if (CaptureStalled(sample, options, now)) {
      return Report(DeviceLivenessStates.NotLive, DeviceLivenessReasons.CaptureStalled);
    }
    if (sample.CaptureLoopRunning
        && unchangedMs > options.StaleLimitMs
        && Since(sample.FirstInputAfterChangeAt, now) > options.StaleLimitMs) {
      return Report(DeviceLivenessStates.NotLive, DeviceLivenessReasons.NoChangeAfterInput);
    }
    if (sample.CaptureLoopRunning && sample.LastCaptureAt is not null) {
      return Report(DeviceLivenessStates.Live, null);
    }
    return Report(DeviceLivenessStates.Unknown, null, needsProbe: true);
  }

  /// <summary>Rule 3: the last input timed out, or it is pending for too long, and no frame change came after it.</summary>
  private static bool InputTimedOut(DeviceLivenessSample sample, DeviceLivenessOptions options, DateTimeOffset now) {
    var timedOut = sample.LastInputOutcome switch {
      InputOutcome.TimedOut => true,
      InputOutcome.Pending => Since(sample.LastInputAt, now) > options.InputTimeoutMs,
      _ => false
    };
    if (!timedOut) return false;
    // A frame change after the start of the input shows that the device is alive again.
    return !(sample.LastChangeAt is { } changed && sample.LastInputAt is { } input && changed > input);
  }

  /// <summary>Rule 4: a capture loop runs, and no capture completed for longer than the stall limit.</summary>
  private static bool CaptureStalled(DeviceLivenessSample sample, DeviceLivenessOptions options, DateTimeOffset now) {
    if (!sample.CaptureLoopRunning) return false;
    return Since(sample.LastCaptureAt ?? sample.LoopStartedAt, now) > options.CaptureStallLimitMs;
  }

  /// <summary>Milliseconds from <paramref name="at"/> to <paramref name="now"/>, at least 0. Null when <paramref name="at"/> is null.</summary>
  private static long? Since(DateTimeOffset? at, DateTimeOffset now) =>
    at is { } value ? Math.Max(0L, (long)(now - value).TotalMilliseconds) : null;
}
