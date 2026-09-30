using System;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Contracts.Queues;
using GameBot.Service.Services.Liveness;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Builds <c>health.deviceLiveness</c> of a running queue (feature 106, T063). The read evaluates the
/// session again and gives the report to the fault episode, so the state that it shows is current and
/// not as old as the last periodic check.
/// </summary>
internal sealed class DeviceLivenessReader {
  private readonly ISessionManager _sessions;
  private readonly ISessionLivenessService _liveness;
  private readonly TimeProvider _time;

  public DeviceLivenessReader(ISessionManager sessions, ISessionLivenessService liveness, TimeProvider time) {
    _sessions = sessions;
    _liveness = liveness;
    _time = time;
  }

  /// <summary>The device liveness of the run, or null when the run has no session yet.</summary>
  /// <param name="handle">The run handle.</param>
  /// <param name="recovery">The recovery settings of the queue. Null when the queue has none.</param>
  public QueueDeviceLivenessResponse? Project(QueueRunHandle handle, QueueDeviceRecovery? recovery = null) {
    ArgumentNullException.ThrowIfNull(handle);
    if (handle.SessionId is not { } sessionId) return null;
    var session = _sessions.GetSession(sessionId);
    if (session is not null) {
      handle.Liveness.Observe(_liveness.Evaluate(session), _time.GetLocalNow());
    }
    var snapshot = handle.Liveness.Snapshot();
    if (snapshot.LastReport is not { } report) return null;
    return new QueueDeviceLivenessResponse {
      State = snapshot.State,
      Reason = snapshot.Reason,
      NotLiveSince = snapshot.NotLiveSince,
      Stale = report.Stale,
      FrameAgeMs = report.FrameAgeMs,
      UnchangedMs = report.UnchangedMs,
      GatedFirings = snapshot.GatedFirings,
      AlertSent = snapshot.AlertSent,
      RecoveryAttempts = snapshot.Attempts,
      RecoveryState = DeriveRecoveryState(snapshot, recovery)
    };
  }

  /// <summary>
  /// The recovery state (feature 121, research R-022): <c>running</c> when an attempt runs, else
  /// <c>exhausted</c> when the attempts reached <c>maxAttempts</c> and the state is <c>not_live</c>, else
  /// <c>idle</c>. It needs no stored field.
  /// </summary>
  internal static string DeriveRecoveryState(QueueLivenessSnapshot snapshot, QueueDeviceRecovery? recovery) {
    if (snapshot.RecoveryRunning) return DeviceRecoveryStates.Running;
    if (recovery is { Reboots: true }
        && snapshot.State == DeviceLivenessStates.NotLive
        && snapshot.Attempts >= recovery.MaxAttempts) {
      return DeviceRecoveryStates.Exhausted;
    }
    return DeviceRecoveryStates.Idle;
  }
}
