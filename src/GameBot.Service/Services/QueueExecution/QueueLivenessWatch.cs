using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.Liveness;
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
/// The watch never stops the run and never recovers the device (FR-018). A stop policy that the operator
/// configured acts through the evaluator, as for each other failed cycle. The watch never throws: it logs
/// each exception and continues.
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

  public QueueLivenessWatch(
    ExecutionQueue queue,
    QueueRunHandle handle,
    string rootId,
    ISessionManager sessions,
    ISessionLivenessService liveness,
    IExecutionLogService log,
    QueueFailurePolicyEvaluator? failurePolicy,
    TimeProvider time,
    ILogger logger) {
    _queue = queue;
    _handle = handle;
    _rootId = rootId;
    _sessions = sessions;
    _liveness = liveness;
    _log = log;
    _failurePolicy = failurePolicy;
    _time = time;
    _logger = logger;
  }

  /// <summary>Runs the check every <c>QueueCheckIntervalMs</c> until <paramref name="ct"/> is cancelled.</summary>
  public async Task RunAsync(CancellationToken ct) {
    var interval = TimeSpan.FromMilliseconds(Math.Max(1, _liveness.Options.QueueCheckIntervalMs));
    while (!ct.IsCancellationRequested) {
      try {
        await Task.Delay(interval, _time, ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        return;
      }
      await CheckOnceAsync().ConfigureAwait(false);
    }
  }

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
