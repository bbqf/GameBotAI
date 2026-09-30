using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.Liveness;
using GameBot.Service.Services.Notifications;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// The periodic device-liveness check of one queue run (feature 106, FR-017, research R-012). Every
/// <c>QueueCheckIntervalMs</c> it evaluates the device of the run and updates the fault episode. When the
/// device stays not live (each reason) for longer than <c>QueueGracePeriodMs</c>, it does these steps one
/// time for the episode:
/// <list type="number">
/// <item>It writes one failed <c>queue</c> entry to the execution log.</item>
/// <item>It seals one failed cycle in the cycle ledger.</item>
/// <item>It gives the cycle to the failure policy evaluator.</item>
/// </list>
/// Feature 121 adds two jobs to each check. It sends one "device not live" alert when the episode is older
/// than <c>AlertAfterMs</c> and one "device live again" message after the device is live. When the queue has
/// <c>deviceRecovery</c> with action <c>reboot-instance</c>, it also starts one recovery attempt in the
/// background when the episode is older than <c>afterMs</c>.
/// The watch never stops the run. The watch repairs the device only through the recovery runner, and only
/// when the queue asks for it. A stop policy that the operator configured acts through the evaluator, as for
/// each other failed cycle. The watch never throws: it logs each exception and continues.
/// </summary>
internal sealed class QueueLivenessWatch {
  private readonly ExecutionQueue _queue;
  private readonly QueueRunHandle _handle;
  private readonly string _rootId;
  private readonly ISessionManager _sessions;
  private readonly ISessionLivenessService _liveness;
  private readonly IExecutionLogService _log;
  private readonly QueueFailurePolicyEvaluator? _failurePolicy;
  private readonly TimeProvider _time;
  private readonly ILogger _logger;
  private readonly INotificationDispatcher? _notifications;
  private readonly QueueDeviceRecoveryRunner? _recovery;
  private readonly Func<ExecutionQueue, QueueRunHandle, Task>? _rebind;
  private CancellationToken _stopToken = CancellationToken.None;
  private Task _recoveryTask = Task.CompletedTask;

  public QueueLivenessWatch(
    ExecutionQueue queue,
    QueueRunHandle handle,
    string rootId,
    ISessionManager sessions,
    ISessionLivenessService liveness,
    IExecutionLogService log,
    QueueFailurePolicyEvaluator? failurePolicy,
    TimeProvider time,
    ILogger logger,
    INotificationDispatcher? notifications = null,
    QueueDeviceRecoveryRunner? recovery = null,
    Func<ExecutionQueue, QueueRunHandle, Task>? rebind = null) {
    _queue = queue;
    _handle = handle;
    _rootId = rootId;
    _sessions = sessions;
    _liveness = liveness;
    _log = log;
    _failurePolicy = failurePolicy;
    _time = time;
    _logger = logger;
    _notifications = notifications;
    _recovery = recovery;
    _rebind = rebind;
  }

  /// <summary>
  /// Runs the check every <c>QueueCheckIntervalMs</c> until <paramref name="ct"/> is cancelled. At the end
  /// it waits for a recovery attempt that still runs (the same token cancels it).
  /// </summary>
  public async Task RunAsync(CancellationToken ct) {
    _stopToken = ct;
    var interval = TimeSpan.FromMilliseconds(Math.Max(1, _liveness.Options.QueueCheckIntervalMs));
    while (!ct.IsCancellationRequested) {
      try {
        await Task.Delay(interval, _time, ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        break;
      }
      await CheckOnceAsync().ConfigureAwait(false);
    }

    try {
      await _recoveryTask.ConfigureAwait(false);
    }
    catch (Exception) {
      // The recovery task logs its own faults.
    }

    FlushLiveAgain();
  }

  // A run can end (or stop) between the moment that the gate of the run loop sees the device live again
  // and the next check of this watch. The "live again" claim is then still open. Send it at the end, so
  // the operator gets the message that belongs to the alert. A stop while the device is still not live
  // has no claim, so it sends nothing.
  private void FlushLiveAgain() {
    try {
      if (_notifications is null || !_handle.Liveness.TryClaimLiveAgain()) return;
      Send(QueueAlertKind.LiveAgain, null, _time.GetLocalNow());
    }
    catch (Exception ex) {
      QueueLivenessLog.WatchFaulted(_logger, _queue.Id, ex);
    }
  }

  /// <summary>The running recovery attempt. Tests wait for it. It is a completed task when none runs.</summary>
  internal Task RecoveryTask => _recoveryTask;

  /// <summary>
  /// One check. Returns true when this check recorded the fault cycle of the episode. Never throws.
  /// </summary>
  public async Task<bool> CheckOnceAsync() {
    try {
      var sessionId = _handle.SessionId;
      if (sessionId is null) return false;
      var session = _sessions.GetSession(sessionId);
      if (session is null) return false;

      var report = _liveness.Evaluate(session);
      var now = _time.GetLocalNow();
      _handle.Liveness.Observe(report, now);
      SendAlerts(report, now);
      StartRecoveryIfDue(report, now);
      var grace = TimeSpan.FromMilliseconds(Math.Max(0, _liveness.Options.QueueGracePeriodMs));
      if (!_handle.Liveness.TryClaimFaultCycle(now, grace)) return false;

      var reason = report.Reason ?? DeviceLivenessStates.NotLive;
      // Read the root at the time of the call: a long run can rotate its log segment.
      var root = _handle.RootExecutionId ?? _rootId;
      // CancellationToken.None: a stop must not leave half an entry.
      await _log.LogQueueDeviceFaultAsync(root, _queue.Id, _queue.Name, reason, CancellationToken.None).ConfigureAwait(false);
      var since = _handle.Liveness.Snapshot().NotLiveSince ?? now;
      _handle.Cycles.RecordFaultCycle(since, now);
      _failurePolicy?.OnCycleCompleted(_queue, _handle);
      QueueLivenessLog.FaultEpisodeRecorded(_logger, _queue.Id, reason);
      return true;
    }
    catch (Exception ex) {
      QueueLivenessLog.WatchFaulted(_logger, _queue.Id, ex);
      return false;
    }
  }

  // Feature 121 (FR-001, FR-002, FR-003): each claim is true one time for the episode, so each message is
  // sent one time. Without a dispatcher the watch claims nothing.
  private void SendAlerts(DeviceLivenessReport report, DateTimeOffset now) {
    if (_notifications is null) return;
    var after = TimeSpan.FromMilliseconds(Math.Max(0, _liveness.Options.AlertAfterMs));
    if (report.State == DeviceLivenessStates.NotLive && _handle.Liveness.TryClaimAlert(now, after)) {
      Send(QueueAlertKind.NotLive, report.Reason ?? DeviceLivenessStates.NotLive, now);
    }
    if (_handle.Liveness.TryClaimLiveAgain()) {
      Send(QueueAlertKind.LiveAgain, null, now);
    }
  }

  private void Send(QueueAlertKind kind, string? reason, DateTimeOffset now) {
    _notifications!.SendAlert(new QueueAlert(
      _queue.Id, kind, reason, now, 0, (at, ok, error) => _handle.RecordNotification(at, ok, error)));
  }

  // Feature 121 (FR-007): start one attempt in the background when the episode is old enough. The
  // episode claim keeps one attempt at a time, and it keeps the attempt count and the cooldown.
  private void StartRecoveryIfDue(DeviceLivenessReport report, DateTimeOffset now) {
    if (_recovery is null || _rebind is null) return;
    if (_queue.DeviceRecovery is not { Reboots: true } settings) return;
    if (report.State != DeviceLivenessStates.NotLive) return;
    var claimed = _handle.Liveness.TryBeginRecovery(
      now,
      TimeSpan.FromMilliseconds(settings.AfterMs),
      settings.MaxAttempts,
      TimeSpan.FromMilliseconds(settings.CooldownMs));
    if (!claimed) return;
    _recoveryTask = Task.Run(() => RunRecoveryAttemptAsync(_recovery, _rebind, _stopToken), CancellationToken.None);
  }

  private async Task RunRecoveryAttemptAsync(
    QueueDeviceRecoveryRunner runner,
    Func<ExecutionQueue, QueueRunHandle, Task> rebind,
    CancellationToken ct) {
    try {
      await runner.RunAttemptAsync(_queue, _handle, rebind, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      // A stop or a delete of the queue. The runner already cleared the running flag.
    }
    catch (Exception ex) {
      QueueLivenessLog.WatchFaulted(_logger, _queue.Id, ex);
    }
  }
}

internal static partial class QueueLivenessLog {
  [LoggerMessage(EventId = 1140, Level = LogLevel.Warning, Message = "Queue {QueueId}: the device is not live ({Reason}) for longer than the grace period. The queue recorded one failed cycle. It holds the firings that need the device.")]
  public static partial void FaultEpisodeRecorded(ILogger logger, string QueueId, string Reason);

  [LoggerMessage(EventId = 1141, Level = LogLevel.Warning, Message = "Queue {QueueId}: the device liveness check failed. The check continues at the next interval.")]
  public static partial void WatchFaulted(ILogger logger, string QueueId, Exception ex);

  [LoggerMessage(EventId = 1142, Level = LogLevel.Warning, Message = "Queue {QueueId}: the device is not live ({Reason}). The queue holds the firing of sequence {SequenceId}.")]
  public static partial void FiringHeld(ILogger logger, string QueueId, string SequenceId, string Reason);

  [LoggerMessage(EventId = 1143, Level = LogLevel.Warning, Message = "Queue {QueueId}: could not record the held firing of sequence {SequenceId}. The firing stays held.")]
  public static partial void GateRecordFailed(ILogger logger, string QueueId, string SequenceId, Exception ex);
}
