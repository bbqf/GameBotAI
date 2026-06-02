using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Emulator.Session;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Executes queues for real: loads the linked template, connects to the bound emulator, runs the
/// template's sequences in order (optionally cycling), and writes one terminating queue-run
/// execution-log entry with the stop reason. Replaces the placeholder start/stop behavior.
/// </summary>
internal sealed class QueueExecutionService : IQueueExecutionService {
  private readonly IQueueRepository _queues;
  private readonly IQueueRuntimeStore _runtime;
  private readonly IQueueTemplateRepository _templates;
  private readonly ISequenceExecutionService _sequenceExecution;
  private readonly ISessionManager _sessions;
  private readonly IExecutionLogService _log;
  private readonly ILogger<QueueExecutionService> _logger;
  private readonly CancellationToken _appStopping;

  private readonly ConcurrentDictionary<string, QueueRunHandle> _runs =
    new(StringComparer.Ordinal);

  public QueueExecutionService(
    IQueueRepository queues,
    IQueueRuntimeStore runtime,
    IQueueTemplateRepository templates,
    ISequenceExecutionService sequenceExecution,
    ISessionManager sessions,
    IExecutionLogService log,
    ILogger<QueueExecutionService> logger,
    IHostApplicationLifetime? lifetime = null) {
    _queues = queues;
    _runtime = runtime;
    _templates = templates;
    _sequenceExecution = sequenceExecution;
    _sessions = sessions;
    _log = log;
    _logger = logger;
    _appStopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
  }

  public bool IsRunning(string queueId) => _runs.ContainsKey(queueId);

  public async Task<QueueStartOutcome> StartAsync(string queueId, CancellationToken ct = default) {
    var queue = await _queues.GetAsync(queueId).ConfigureAwait(false);
    if (queue is null) return QueueStartOutcome.NotFound;

    var cts = CancellationTokenSource.CreateLinkedTokenSource(_appStopping);
    var handle = new QueueRunHandle { QueueId = queueId, Cts = cts };
    if (!_runs.TryAdd(queueId, handle)) {
      cts.Dispose();
      return QueueStartOutcome.AlreadyRunning;
    }

    _runtime.SetStatus(queueId, QueueExecutionStatus.Running);
    handle.RunTask = Task.Run(() => RunAsync(queue, handle, cts.Token), CancellationToken.None);
    return QueueStartOutcome.Started;
  }

  public async Task StopAsync(string queueId, CancellationToken ct = default) {
    if (!_runs.TryGetValue(queueId, out var handle)) return; // not running → no-op (FR-022)
    try {
      await handle.Cts.CancelAsync().ConfigureAwait(false);
    }
    catch (ObjectDisposedException) {
      // run already completed and disposed its CTS; nothing to cancel
    }
    // Wait for the run to settle (disconnect + terminating entry + status reset) so callers
    // observe a fully-stopped queue. The run aborts promptly, well within the stop budget.
    try {
      await handle.RunTask.ConfigureAwait(false);
    }
    catch {
      // run faults are recorded in the run's own finalize/logging; stop itself never throws
    }
  }

  private async Task RunAsync(ExecutionQueue queue, QueueRunHandle handle, CancellationToken ct) {
    var rootId = await _log.LogQueueStartAsync(queue.Id, queue.Name, CancellationToken.None).ConfigureAwait(false);
    handle.RootExecutionId = rootId;

    var reason = QueueStopReason.CompletedFullRun;
    string? failureReason = null;
    var executed = 0;
    var failed = 0;
    var cycles = 0;
    string? sessionId = null;

    try {
      // 1. Load the linked template server-side (FR-002).
      var template = string.IsNullOrEmpty(queue.LinkedTemplateId)
        ? null
        : await _templates.GetAsync(queue.LinkedTemplateId).ConfigureAwait(false);
      if (template is null) {
        reason = QueueStopReason.Failure;
        failureReason = "no template to run (the queue has no linked template, or it could not be resolved)";
      }
      else {
        var snapshot = template.Entries.Select(e => e.SequenceId).ToList();

        // 2. Connect to the bound emulator (FR-003/FR-004).
        try {
          var session = _sessions.CreateSession($"queue:{queue.Id}", queue.EmulatorSerial);
          sessionId = session.Id;
          handle.SessionId = sessionId;
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) {
          reason = QueueStopReason.Failure;
          failureReason = $"emulator could not be reached ('{queue.EmulatorSerial}'): {ex.Message}";
        }

        // 3. Run the sequences in order, optionally cycling (FR-006/FR-014/FR-016/FR-017).
        if (sessionId is not null) {
          try {
            if (snapshot.Count > 0) {
              var index = 0;
              do {
                ct.ThrowIfCancellationRequested();
                foreach (var sequenceId in snapshot) {
                  ct.ThrowIfCancellationRequested();
                  // The emulator session vanishing mid-run is a run-level failure: remaining
                  // sequences cannot run (FR-008a).
                  if (_sessions.GetSession(sessionId) is null) {
                    throw new QueueConnectionLostException();
                  }
                  var ok = await RunOneSequenceAsync(sequenceId, rootId, ++index, sessionId, ct).ConfigureAwait(false);
                  executed++;
                  if (!ok) failed++;
                }
                cycles++;
              } while (queue.CycleExecution);
            }
            else {
              // Empty template: a full pass with no work; never busy-loop when cycling (FR-017).
              cycles = 1;
            }
            reason = QueueStopReason.CompletedFullRun;
          }
          catch (QueueConnectionLostException) {
            reason = QueueStopReason.Failure;
            failureReason = $"emulator connection lost mid-run ('{queue.EmulatorSerial}')";
          }
          catch (OperationCanceledException) {
            reason = QueueStopReason.StoppedManually;
          }
        }
      }
    }
    catch (OperationCanceledException) {
      reason = QueueStopReason.StoppedManually;
    }
    catch (Exception ex) {
      reason = QueueStopReason.Failure;
      failureReason = ex.Message;
      QueueExecutionLog.RunFaulted(_logger, queue.Id, ex);
    }
    finally {
      // Always disconnect the session (FR-020/FR-023) — teardown errors must not prevent
      // finalizing the run (FR-023 edge case).
      if (sessionId is not null) {
        try { _sessions.StopSession(sessionId); }
        catch (Exception ex) { QueueExecutionLog.DisconnectFailed(_logger, queue.Id, ex); }
      }

      var result = new QueueRunResult(reason, executed, failed, cycles, failureReason);
      var finalStatus = reason == QueueStopReason.Failure ? "failure" : "success";
      try {
        await _log.LogQueueFinalizeAsync(rootId, queue.Id, queue.Name, finalStatus, BuildSummary(queue.Name, result), ct: CancellationToken.None).ConfigureAwait(false);
      }
      catch (Exception ex) { QueueExecutionLog.FinalizeFailed(_logger, queue.Id, ex); }

      _runtime.SetStatus(queue.Id, QueueExecutionStatus.Stopped);
      _runs.TryRemove(queue.Id, out _);
      handle.Cts.Dispose();
    }
  }

  /// <summary>
  /// Runs one sequence as a child of the queue run. Per-sequence failures are non-fatal (FR-008):
  /// returns false on a failed/unresolved sequence so the run can continue.
  /// </summary>
  private async Task<bool> RunOneSequenceAsync(string sequenceId, string rootId, int index, string sessionId, CancellationToken ct) {
    try {
      var parentContext = new ExecutionLogContext {
        ParentExecutionId = rootId,
        RootExecutionId = rootId,
        Depth = 1,
        SequenceIndex = index
      };
      var res = await _sequenceExecution.ExecuteAsync(sequenceId, sessionId, parentContext, ct).ConfigureAwait(false);
      return string.Equals(res.Status, "Succeeded", StringComparison.OrdinalIgnoreCase);
    }
    catch (OperationCanceledException) {
      throw; // a stop request must propagate to abort the run
    }
    catch (Exception ex) {
      // Unexpected per-sequence error (e.g. a stale/unresolved reference): non-fatal (FR-008/008b).
      QueueExecutionLog.SequenceFaulted(_logger, sequenceId, ex);
      return false;
    }
  }

  /// <summary>Signals that the bound emulator session disappeared while the run was in progress.</summary>
  private sealed class QueueConnectionLostException : Exception {
    public QueueConnectionLostException() { }
    public QueueConnectionLostException(string message) : base(message) { }
    public QueueConnectionLostException(string message, Exception innerException) : base(message, innerException) { }
  }

  private static string BuildSummary(string queueName, QueueRunResult r) {
    var failedNote = r.SequencesFailed > 0 ? $", {r.SequencesFailed} failed" : string.Empty;
    return r.StopReason switch {
      QueueStopReason.CompletedFullRun =>
        $"Queue '{queueName}' completed full run: {r.SequencesExecuted} sequence(s) executed{failedNote}{(r.Cycles > 1 ? $" across {r.Cycles} cycles" : string.Empty)}.",
      QueueStopReason.StoppedManually =>
        $"Queue '{queueName}' stopped manually after {r.SequencesExecuted} sequence(s) executed{failedNote}.",
      _ =>
        $"Queue '{queueName}' failed: {r.FailureReason ?? "unknown error"}."
    };
  }
}

internal static partial class QueueExecutionLog {
  [LoggerMessage(EventId = 1110, Level = LogLevel.Error, Message = "Queue {QueueId} run faulted")]
  public static partial void RunFaulted(ILogger logger, string QueueId, Exception ex);

  [LoggerMessage(EventId = 1111, Level = LogLevel.Warning, Message = "Queue {QueueId} failed to disconnect emulator session during teardown")]
  public static partial void DisconnectFailed(ILogger logger, string QueueId, Exception ex);

  [LoggerMessage(EventId = 1112, Level = LogLevel.Warning, Message = "Queue {QueueId} failed to finalize run log entry")]
  public static partial void FinalizeFailed(ILogger logger, string QueueId, Exception ex);

  [LoggerMessage(EventId = 1113, Level = LogLevel.Warning, Message = "Sequence {SequenceId} faulted during queue run; treated as a non-fatal failure")]
  public static partial void SequenceFaulted(ILogger logger, string SequenceId, Exception ex);
}
