using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.Liveness;
using GameBot.Service.Services.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// The recovery of one queue (feature 121, FR-007 to FR-009, research R-008, R-010). One call of
/// <see cref="RunAttemptAsync"/> is one attempt:
/// <list type="number">
/// <item>It asks the coordinator to reboot the instance (the coordinator owns the slot and the stagger time).</item>
/// <item>It waits for the device to answer (adb state <c>device</c> and boot complete).</item>
/// <item>It rebinds the session of the queue.</item>
/// <item>It waits for the liveness state <c>live</c> of the new session.</item>
/// </list>
/// Each wait ends at <c>RebootReadyTimeoutMs</c>. The runner updates the attempt data of the episode. After
/// the last allowed attempt fails, it claims and sends one "recovery failed" alert. The runner never stops
/// the queue. Registered as a singleton: it keeps no state for one queue.
/// </summary>
internal sealed class QueueDeviceRecoveryRunner {
  private static readonly TimeSpan DefaultProbePoll = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan DefaultLivePoll = TimeSpan.FromSeconds(2);

  private readonly IDeviceRecoveryCoordinator _coordinator;
  private readonly IEmulatorDeviceProbe _probe;
  private readonly ISessionManager _sessions;
  private readonly ISessionLivenessService _liveness;
  private readonly INotificationDispatcher? _notifications;
  private readonly TimeProvider _time;
  private readonly ILogger _logger;
  private readonly TimeSpan _probePoll;
  private readonly TimeSpan _livePoll;

  public QueueDeviceRecoveryRunner(
    IDeviceRecoveryCoordinator coordinator,
    IEmulatorDeviceProbe probe,
    ISessionManager sessions,
    ISessionLivenessService liveness,
    INotificationDispatcher? notifications = null,
    TimeProvider? time = null,
    ILogger<QueueDeviceRecoveryRunner>? logger = null)
    : this(coordinator, probe, sessions, liveness, notifications, time, (ILogger?)logger, null, null) {
  }

  /// <summary>Test seam: the poll intervals can be short.</summary>
  internal QueueDeviceRecoveryRunner(
    IDeviceRecoveryCoordinator coordinator,
    IEmulatorDeviceProbe probe,
    ISessionManager sessions,
    ISessionLivenessService liveness,
    INotificationDispatcher? notifications,
    TimeProvider? time,
    ILogger? logger,
    TimeSpan? probePoll,
    TimeSpan? livePoll) {
    _coordinator = coordinator;
    _probe = probe;
    _sessions = sessions;
    _liveness = liveness;
    _notifications = notifications;
    _time = time ?? TimeProvider.System;
    _logger = logger ?? NullLogger.Instance;
    _probePoll = probePoll ?? DefaultProbePoll;
    _livePoll = livePoll ?? DefaultLivePoll;
  }

  /// <summary>
  /// Runs one attempt for <paramref name="queue"/>. The caller has claimed the attempt with
  /// <see cref="QueueLivenessEpisode.TryBeginRecovery"/>. Returns true when the device is live after the
  /// attempt. Returns false for each failure (also an exception of a step). A cancel of
  /// <paramref name="ct"/> (a stop or a delete of the queue) throws <see cref="OperationCanceledException"/>
  /// and clears the running flag with no count.
  /// </summary>
  /// <param name="queue">The queue. It needs a non-blank <c>EmulatorInstanceName</c>.</param>
  /// <param name="handle">The run handle of the queue.</param>
  /// <param name="rebind">Makes a new session for the queue and sets <c>handle.SessionId</c>.</param>
  /// <param name="ct">The token of the queue run.</param>
  public async Task<bool> RunAttemptAsync(
    ExecutionQueue queue,
    QueueRunHandle handle,
    Func<ExecutionQueue, QueueRunHandle, Task> rebind,
    CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(queue);
    ArgumentNullException.ThrowIfNull(handle);
    ArgumentNullException.ThrowIfNull(rebind);

    var success = false;
    try {
      success = await ExecuteStepsAsync(queue, handle, rebind, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      handle.Liveness.AbortRecoveryAttempt();
      throw;
    }
    catch (Exception ex) {
      RecoveryLog.AttemptFaulted(_logger, queue.Id, ex);
    }

    handle.Liveness.EndRecoveryAttempt(_time.GetLocalNow());
    if (!success) SendRecoveryFailedIfUsedUp(queue, handle);
    return success;
  }

  private async Task<bool> ExecuteStepsAsync(
    ExecutionQueue queue,
    QueueRunHandle handle,
    Func<ExecutionQueue, QueueRunHandle, Task> rebind,
    CancellationToken ct) {
    var options = _liveness.Options;
    var limit = TimeSpan.FromMilliseconds(Math.Max(1, options.RebootReadyTimeoutMs));
    var instance = queue.EmulatorInstanceName;
    if (string.IsNullOrWhiteSpace(instance)) {
      RecoveryLog.NoInstanceName(_logger, queue.Id);
      return false;
    }

    RecoveryLog.AttemptStarted(_logger, queue.Id, instance);
    var rebooted = await _coordinator.RebootInstanceAsync(instance, ct).ConfigureAwait(false);
    if (!rebooted) {
      RecoveryLog.RebootFailed(_logger, queue.Id, instance);
      return false;
    }

    if (!await WaitForDeviceAsync(queue.EmulatorSerial, limit, ct).ConfigureAwait(false)) {
      RecoveryLog.DeviceNotReady(_logger, queue.Id, queue.EmulatorSerial);
      return false;
    }

    await rebind(queue, handle).ConfigureAwait(false);

    if (!await WaitForLiveAsync(handle, limit, ct).ConfigureAwait(false)) {
      RecoveryLog.StillNotLive(_logger, queue.Id);
      return false;
    }

    RecoveryLog.AttemptSucceeded(_logger, queue.Id);
    return true;
  }

  private async Task<bool> WaitForDeviceAsync(string serial, TimeSpan limit, CancellationToken ct) {
    var deadline = _time.GetUtcNow() + limit;
    while (true) {
      if (await _probe.IsResponsiveAsync(serial, ct).ConfigureAwait(false)) return true;
      var remaining = deadline - _time.GetUtcNow();
      if (remaining <= TimeSpan.Zero) return false;
      await Task.Delay(remaining < _probePoll ? remaining : _probePoll, _time, ct).ConfigureAwait(false);
    }
  }

  private async Task<bool> WaitForLiveAsync(QueueRunHandle handle, TimeSpan limit, CancellationToken ct) {
    var deadline = _time.GetUtcNow() + limit;
    while (true) {
      var sessionId = handle.SessionId;
      var session = sessionId is null ? null : _sessions.GetSession(sessionId);
      if (session is not null && _liveness.Evaluate(session).State == DeviceLivenessStates.Live) return true;
      var remaining = deadline - _time.GetUtcNow();
      if (remaining <= TimeSpan.Zero) return false;
      await Task.Delay(remaining < _livePoll ? remaining : _livePoll, _time, ct).ConfigureAwait(false);
    }
  }

  private void SendRecoveryFailedIfUsedUp(ExecutionQueue queue, QueueRunHandle handle) {
    var maxAttempts = queue.DeviceRecovery?.MaxAttempts ?? QueueDeviceRecovery.DefaultMaxAttempts;
    var snapshot = handle.Liveness.Snapshot();
    if (snapshot.State != DeviceLivenessStates.NotLive || snapshot.Attempts < maxAttempts) return;
    if (_notifications is null || !handle.Liveness.TryClaimRecoveryFailed()) return;
    _notifications.SendAlert(new QueueAlert(
      queue.Id,
      QueueAlertKind.RecoveryFailed,
      null,
      _time.GetLocalNow(),
      snapshot.Attempts,
      (at, ok, error) => handle.RecordNotification(at, ok, error)));
  }
}

internal static partial class RecoveryLog {
  [LoggerMessage(EventId = 1150, Level = LogLevel.Warning, Message = "Queue {QueueId}: device recovery starts an attempt. The service reboots instance {Instance}.")]
  public static partial void AttemptStarted(ILogger logger, string QueueId, string Instance);

  [LoggerMessage(EventId = 1151, Level = LogLevel.Warning, Message = "Queue {QueueId}: device recovery failed. The reboot command of instance {Instance} did not end with exit code 0, or the tool is missing.")]
  public static partial void RebootFailed(ILogger logger, string QueueId, string Instance);

  [LoggerMessage(EventId = 1152, Level = LogLevel.Warning, Message = "Queue {QueueId}: device recovery failed. Device {Serial} did not answer in time after the reboot.")]
  public static partial void DeviceNotReady(ILogger logger, string QueueId, string Serial);

  [LoggerMessage(EventId = 1153, Level = LogLevel.Warning, Message = "Queue {QueueId}: device recovery failed. The device is not live in time after the session rebind.")]
  public static partial void StillNotLive(ILogger logger, string QueueId);

  [LoggerMessage(EventId = 1154, Level = LogLevel.Information, Message = "Queue {QueueId}: device recovery succeeded. The device is live again.")]
  public static partial void AttemptSucceeded(ILogger logger, string QueueId);

  [LoggerMessage(EventId = 1155, Level = LogLevel.Warning, Message = "Queue {QueueId}: a device recovery attempt failed with an exception.")]
  public static partial void AttemptFaulted(ILogger logger, string QueueId, Exception ex);

  [LoggerMessage(EventId = 1156, Level = LogLevel.Warning, Message = "Queue {QueueId}: device recovery needs emulatorInstanceName. The attempt is skipped.")]
  public static partial void NoInstanceName(ILogger logger, string QueueId);
}
