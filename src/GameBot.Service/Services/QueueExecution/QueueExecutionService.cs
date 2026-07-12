using System;
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
///
/// Schedule types:
///   AtQueueStart — executed once at run start, in template order, before any timer evaluation and
///                 before the first OncePerRun step; counts toward executed (feature 060).
///   OncePerRun  — executed in template order as the regular "step"; defines run completion.
///   EveryStep   — executed after each OncePerRun step (and after the final step); not counted.
///   Timer       — evaluated at each iteration boundary; either an absolute time-of-day (fires at
///                 most once per calendar day) or a relative offset / live schedule (feature 059).
///
/// A cycling run re-evaluates timers on every cycle. A non-cyclic run runs its once-per-run steps
/// once and then, if any relative-offset timer or live schedule is still pending, stays alive and
/// keeps re-evaluating (polling) until those fire or the run is stopped — so a "+10s" relative timer
/// on a non-cyclic queue actually becomes due instead of the run completing instantly (feature 059).
/// </summary>
internal sealed class QueueExecutionService : IQueueExecutionService {
  private readonly IQueueRepository _queues;
  private readonly IQueueRuntimeStore _runtime;
  private readonly IQueueTemplateRepository _templates;
  private readonly ISequenceExecutionService _sequenceExecution;
  private readonly ISessionManager _sessions;
  private readonly BackgroundScreenCaptureService? _captureService;
  private readonly IExecutionLogService _log;
  private readonly ILogger<QueueExecutionService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly CancellationToken _appStopping;
  private readonly IQueueRunRegistry _registry;

  // How often a non-cyclic run re-checks pending relative/live timers while waiting for one to become
  // due. Small enough that a firing lands within roughly an iteration interval of the offset, large
  // enough to avoid a busy-wait. (feature 059)
  private static readonly TimeSpan RelativeTimerPollInterval = TimeSpan.FromMilliseconds(250);

  public QueueExecutionService(
    IQueueRepository queues,
    IQueueRuntimeStore runtime,
    IQueueTemplateRepository templates,
    ISequenceExecutionService sequenceExecution,
    ISessionManager sessions,
    IExecutionLogService log,
    ILogger<QueueExecutionService> logger,
    IQueueRunRegistry registry,
    IHostApplicationLifetime? lifetime = null,
    BackgroundScreenCaptureService? captureService = null,
    TimeProvider? timeProvider = null) {
    _queues = queues;
    _runtime = runtime;
    _templates = templates;
    _sequenceExecution = sequenceExecution;
    _sessions = sessions;
    _captureService = captureService;
    _log = log;
    _logger = logger;
    _registry = registry;
    _timeProvider = timeProvider ?? TimeProvider.System;
    _appStopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
  }

  public bool IsRunning(string queueId) => _registry.IsRunning(queueId);

  public LiveScheduleResult ScheduleRelative(string queueId, string sequenceId, TimeSpan offset) {
    if (!_registry.TryGet(queueId, out var handle))
      return new LiveScheduleResult(LiveScheduleOutcome.NotRunning, default);

    var fireAt = _timeProvider.GetLocalNow() + offset;
    // Upsert: a new schedule for the same sequence replaces a still-pending one (FR-011).
    handle.PendingLiveSchedules[sequenceId] = fireAt;
    return new LiveScheduleResult(LiveScheduleOutcome.Scheduled, fireAt);
  }

  public async Task<QueueStartOutcome> StartAsync(string queueId, CancellationToken ct = default) {
    var queue = await _queues.GetAsync(queueId).ConfigureAwait(false);
    if (queue is null) return QueueStartOutcome.NotFound;

    var cts = CancellationTokenSource.CreateLinkedTokenSource(_appStopping);
    var handle = new QueueRunHandle { QueueId = queueId, Cts = cts, CycleExecution = queue.CycleExecution };
    if (!_registry.TryAdd(queueId, handle)) {
      cts.Dispose();
      return QueueStartOutcome.AlreadyRunning;
    }

    // Resolve the linked template once and materialize its entries into the runtime store so the
    // entries shown by GET match what the run executes. The run reuses this same snapshot; the
    // display layer instead reads the runtime store, and auto-load on display is suppressed once
    // the queue is Running (see QueuesEndpoints.MaybeAutoLoadAsync). Without this, a queue started
    // before it was ever displayed reports zero entries despite a populated linked template.
    var template = string.IsNullOrEmpty(queue.LinkedTemplateId)
      ? null
      : await _templates.GetAsync(queue.LinkedTemplateId).ConfigureAwait(false);
    if (template is not null) {
      _runtime.SetEntries(queueId, template.Entries.Select(e => e.SequenceId));
    }

    _runtime.SetStatus(queueId, QueueExecutionStatus.Running);
    handle.RunTask = Task.Run(() => RunAsync(queue, template, handle, cts.Token), CancellationToken.None);
    return QueueStartOutcome.Started;
  }

  public async Task StopAsync(string queueId, CancellationToken ct = default) {
    if (!_registry.TryGet(queueId, out var handle)) return; // not running → no-op (FR-022)
    try {
      await handle.Cts.CancelAsync().ConfigureAwait(false);
    }
    catch (ObjectDisposedException) {
      // run already completed and disposed its CTS; nothing to cancel
    }
    try {
      await handle.RunTask.ConfigureAwait(false);
    }
    catch {
      // run faults are recorded in the run's own finalize/logging; stop itself never throws
    }
  }

  private async Task RunAsync(ExecutionQueue queue, QueueTemplate? template, QueueRunHandle handle, CancellationToken ct) {
    var rootId = await _log.LogQueueStartAsync(queue.Id, queue.Name, CancellationToken.None).ConfigureAwait(false);
    handle.RootExecutionId = rootId;

    var reason = QueueStopReason.CompletedFullRun;
    string? failureReason = null;
    var executed = 0;
    var failed = 0;
    var cycles = 0;
    string? sessionId = null;

    try {
      // 1. Template was resolved once by StartAsync (FR-002) and reused here for the whole run.
      if (template is null) {
        reason = QueueStopReason.Failure;
        failureReason = "no template to run (the queue has no linked template, or it could not be resolved)";
      }
      else {
        // Pre-partition entries by schedule type (FR-001). Snapshots taken once at run start.
        var allEntries = template.Entries.ToList();
        var atQueueStartEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.AtQueueStart).ToList();
        var oncePerRunEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.OncePerRun).ToList();
        var everyStepEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.EveryStep).ToList();
        var timerEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.Timer).ToList();

        // 2. Connect to the bound emulator (FR-003/FR-004).
        try {
          var session = _sessions.CreateSession($"queue:{queue.Id}", queue.EmulatorSerial);
          sessionId = session.Id;
          handle.SessionId = sessionId;
          if (_captureService is not null && !string.IsNullOrWhiteSpace(session.DeviceSerial)) {
            _captureService.StartCapture(session.Id, session.DeviceSerial);
          }
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) {
          reason = QueueStopReason.Failure;
          failureReason = $"emulator could not be reached ('{queue.EmulatorSerial}'): {ex.Message}";
        }

        // 3. Run sequences in order, respecting schedule types, optionally cycling.
        if (sessionId is not null) {
          try {
            var index = 0;

            // (0) At-queue-start pre-pass (feature 060, FR-003/FR-004/FR-007/FR-014/FR-015).
            // Run every at-queue-start entry once, in template order, BEFORE any timer evaluation
            // and before the first OncePerRun step. Runs once per run (outside the do/while, so it
            // never repeats on a cycling queue). Each firing COUNTS toward `executed`; a failure is
            // non-fatal (recorded in `failed`, run continues), consistent with OncePerRun handling.
            foreach (var startEntry in atQueueStartEntries) {
              ct.ThrowIfCancellationRequested();
              if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
              var startOk = await RunOneSequenceAsync(startEntry.SequenceId, rootId, ++index, sessionId, queue.Id, ct).ConfigureAwait(false);
              executed++;
              if (!startOk) failed++;
            }

            // Per-run timer state: maps timer-entry index → last-fired calendar date (FR-003/FR-012).
            // Declared OUTSIDE the do-while loop so it persists across all cycles of this run.
            var timerFiredDate = new Dictionary<int, DateOnly>();

            // Relative-offset timer state (feature 059): the run-start anchor and the set of
            // relative-timer indices already fired this run (fire-once-per-run, FR-005). Both live
            // outside the loop so they survive cycles. Recomputed fresh on every run.
            var runStartedAt = _timeProvider.GetLocalNow();
            handle.RunStartedAt = runStartedAt;
            var relativeTimerFired = new HashSet<int>();
            // Tracks whether the once-per-run/every-step pass has executed. For a non-cyclic run the
            // pass runs once; later loop iterations exist only to wait for pending relative/live
            // timers and must not re-run those steps (feature 059).
            var oncePerRunDone = false;

            // True while a relative-offset timer (template) or a live schedule is still pending and
            // could yet fire — keeps a non-cyclic run alive until its scheduled firings land.
            bool HasPendingRelativeOrLive() {
              for (var pi = 0; pi < timerEntries.Count; pi++) {
                if (timerEntries[pi].TimerRelativeOffset is not null && !relativeTimerFired.Contains(pi))
                  return true;
              }
              if (!handle.PendingLiveSchedules.IsEmpty) return true;
              // feature 065: a self-reschedule Timer firing not yet due keeps a non-cyclic run alive
              // until it lands (or the run is stopped), exactly like a relative/live schedule.
              return handle.HasPendingTimerFirings;
            }

            if (oncePerRunEntries.Count > 0 || everyStepEntries.Count > 0 || timerEntries.Count > 0) {
              do {
                ct.ThrowIfCancellationRequested();

                // (a0) Self-reschedule AtQueueStart firings (feature 065, FR-009): entries queued
                // during the previous cycle fire at the top of the next cycle, before timers and the
                // once-per-run pass. Count toward executed; a failed firing is non-fatal.
                while (handle.PendingNextCycleStart.TryDequeue(out var nextCycleEntry)) {
                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  var nextOk = await RunOneSequenceAsync(nextCycleEntry.SequenceId, rootId, ++index, sessionId, queue.Id, ct, nextCycleEntry.Id).ConfigureAwait(false);
                  executed++;
                  if (!nextOk) failed++;
                }

                // (a) Evaluate timer entries at iteration boundary (FR-011/FR-012/FR-016).
                for (var ti = 0; ti < timerEntries.Count; ti++) {
                  var timerEntry = timerEntries[ti];
                  if (timerEntry.TimerTimeOfDay is null) continue;

                  var localNow = _timeProvider.GetLocalNow();
                  var today = DateOnly.FromDateTime(localNow.DateTime);
                  var now = TimeOnly.FromDateTime(localNow.DateTime);
                  if (now >= timerEntry.TimerTimeOfDay.Value
                      && (!timerFiredDate.TryGetValue(ti, out var lastFired) || lastFired != today)) {
                    ct.ThrowIfCancellationRequested();
                    if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                    var timerOk = await RunOneSequenceAsync(timerEntry.SequenceId, rootId, ++index, sessionId, queue.Id, ct).ConfigureAwait(false);
                    if (!timerOk) failed++;
                    // Timer executions do not count toward `executed` (SC-002 analogue for timers)
                    timerFiredDate[ti] = today;
                  }
                }

                // (a2) Evaluate relative-offset timers at the iteration boundary (feature 059).
                // Fire once per run when elapsed-since-run-start >= offset (FR-005). Relative firings
                // COUNT toward `executed` (FR-016a), unlike time-of-day timers. A failed firing is
                // non-fatal: recorded in `failed`, run continues (FR-016).
                var elapsedSinceStart = _timeProvider.GetLocalNow() - runStartedAt;
                for (var ri = 0; ri < timerEntries.Count; ri++) {
                  var relEntry = timerEntries[ri];
                  if (relEntry.TimerRelativeOffset is not { } relOffset) continue;
                  if (relativeTimerFired.Contains(ri)) continue;
                  if (elapsedSinceStart < relOffset) continue;

                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  var relOk = await RunOneSequenceAsync(relEntry.SequenceId, rootId, ++index, sessionId, queue.Id, ct).ConfigureAwait(false);
                  executed++;
                  if (!relOk) failed++;
                  relativeTimerFired.Add(ri);
                }

                // (a3) Evaluate live relative schedules at the iteration boundary (feature 059).
                // Snapshot the entries that are now due (fireAt <= now), fire each once, then remove
                // it (fires once, FR-009). Live firings COUNT toward `executed` (FR-016a); a failed
                // firing is non-fatal (FR-016). May target any library sequence (FR-013).
                var liveNow = _timeProvider.GetLocalNow();
                foreach (var due in handle.PendingLiveSchedules
                           .Where(kv => kv.Value <= liveNow)
                           .Select(kv => kv.Key)
                           .ToList()) {
                  if (!handle.PendingLiveSchedules.TryRemove(due, out _)) continue;
                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  var liveOk = await RunOneSequenceAsync(due, rootId, ++index, sessionId, queue.Id, ct).ConfigureAwait(false);
                  executed++;
                  if (!liveOk) failed++;
                }

                // (a4) Self-reschedule Timer firings (feature 065, FR-005/FR-006): fire those whose
                // resolved instant is at/before now, once each, then remove. Count toward executed; a
                // failed firing is non-fatal. Entries never due before the run ends are discarded with
                // the handle and never fail the run (FR-015).
                foreach (var timerFiring in handle.DrainDueTimerFirings(_timeProvider.GetLocalNow())) {
                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  var srTimerOk = await RunOneSequenceAsync(timerFiring.SequenceId, rootId, ++index, sessionId, queue.Id, ct, timerFiring.Id).ConfigureAwait(false);
                  executed++;
                  if (!srTimerOk) failed++;
                }

                // (b) OncePerRun steps, each followed by all EveryStep sequences (FR-006/FR-007/FR-016).
                // A cycling run executes these every cycle; a non-cyclic run executes them once, so the
                // relative/live timer-wait passes below never re-run the once-per-run steps.
                if (queue.CycleExecution || !oncePerRunDone) {
                  if (oncePerRunEntries.Count > 0) {
                    foreach (var entry in oncePerRunEntries) {
                      ct.ThrowIfCancellationRequested();
                      if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                      var ok = await RunOneSequenceAsync(entry.SequenceId, rootId, ++index, sessionId, queue.Id, ct).ConfigureAwait(false);
                      executed++;
                      if (!ok) failed++;

                      // Run every-step sequences after each OncePerRun step (FR-006).
                      foreach (var esEntry in everyStepEntries) {
                        ct.ThrowIfCancellationRequested();
                        if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                        var esOk = await RunOneSequenceAsync(esEntry.SequenceId, rootId, ++index, sessionId, queue.Id, ct).ConfigureAwait(false);
                        if (!esOk) failed++;
                        // Every-step executions do not count toward `executed` (FR-008/SC-002).
                      }

                      // Self-reschedule EveryStep injections (feature 065, FR-008): fire after each
                      // once-per-run step for the rest of the run. Snapshot first so a firing's own
                      // re-registration cannot grow the pass (loop-safe). Not counted toward executed.
                      foreach (var injection in handle.EveryStepInjections.Values.ToList()) {
                        ct.ThrowIfCancellationRequested();
                        if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                        var injOk = await RunOneSequenceAsync(injection.SequenceId, rootId, ++index, sessionId, queue.Id, ct, injection.Id).ConfigureAwait(false);
                        if (!injOk) failed++;
                      }
                    }
                  }
                  else if (everyStepEntries.Count > 0) {
                    // FR-009: no OncePerRun entries — EveryStep runs exactly once, then the run ends.
                    foreach (var esEntry in everyStepEntries) {
                      ct.ThrowIfCancellationRequested();
                      if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                      var esOk = await RunOneSequenceAsync(esEntry.SequenceId, rootId, ++index, sessionId, queue.Id, ct).ConfigureAwait(false);
                      if (!esOk) failed++;
                    }
                  }

                  // OncePerRun self-reschedule firings (feature 065, FR-007), and the non-cycling
                  // AtQueueStart fallback: drain a snapshot of those queued this cycle and fire each
                  // before the cycle ends. Count toward executed; failures are non-fatal. Snapshotting
                  // bounds a single drain so an always-true self-reschedule cannot spin within one cycle
                  // (further generations fire next cycle / are abandoned at run end — FR-015).
                  var oncePerRunReschedules = new List<SelfRescheduleEntry>();
                  while (handle.PendingOncePerRun.TryDequeue(out var oprEntry)) oncePerRunReschedules.Add(oprEntry);
                  foreach (var oprFiring in oncePerRunReschedules) {
                    ct.ThrowIfCancellationRequested();
                    if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                    var oprOk = await RunOneSequenceAsync(oprFiring.SequenceId, rootId, ++index, sessionId, queue.Id, ct, oprFiring.Id).ConfigureAwait(false);
                    executed++;
                    if (!oprOk) failed++;
                  }

                  oncePerRunDone = true;
                  cycles++;
                }

                // A cycling run loops immediately (existing behavior). A non-cyclic run breaks once its
                // once-per-run steps are done UNLESS a relative-offset timer or live schedule is still
                // pending — in which case it stays alive, polling, until those fire or it is stopped.
                // Without this a non-cyclic run would finish instantly and a "+10s" relative timer (or
                // live schedule) would never become due (feature 059 fix).
                if (queue.CycleExecution) continue;
                if (!HasPendingRelativeOrLive()) break;
                await Task.Delay(RelativeTimerPollInterval, ct).ConfigureAwait(false);
              } while (true);
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
      // Always disconnect the session (FR-020/FR-023).
      if (sessionId is not null) {
        try { _captureService?.StopCapture(sessionId); }
        catch (Exception ex) { QueueExecutionLog.DisconnectFailed(_logger, queue.Id, ex); }
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
      _registry.Remove(queue.Id, out _);
      handle.Cts.Dispose();
    }
  }

  /// <summary>
  /// Runs one sequence as a child of the queue run. Per-sequence failures are non-fatal (FR-008):
  /// returns false on a failed/unresolved sequence so the run can continue.
  /// </summary>
  private async Task<bool> RunOneSequenceAsync(string sequenceId, string rootId, int index, string sessionId, string queueId, CancellationToken ct, string? selfRescheduleOriginActionId = null) {
    try {
      var parentContext = new ExecutionLogContext {
        ParentExecutionId = rootId,
        RootExecutionId = rootId,
        Depth = 1,
        SequenceIndex = index,
        // Mark this firing as queue-originated so a self-reschedule action can target this run
        // (FR-018); also carry the originating action id for attribution of self-reschedule firings.
        OriginatingQueueId = queueId,
        SelfRescheduleOriginActionId = selfRescheduleOriginActionId
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

  private static string BuildSummary(string queueName, QueueRunResult r) {
    var failedNote = r.SequencesFailed > 0 ? $", {r.SequencesFailed} failed" : string.Empty;
    var cycleNote = r.Cycles > 1 ? $" across {r.Cycles} cycles" : string.Empty;
    return r.StopReason switch {
      QueueStopReason.CompletedFullRun =>
        $"Queue '{queueName}' completed full run: {r.SequencesExecuted} sequence(s) executed{failedNote}{cycleNote}.",
      QueueStopReason.StoppedManually =>
        $"Queue '{queueName}' stopped manually after {r.SequencesExecuted} sequence(s) executed{failedNote}.",
      _ =>
        $"Queue '{queueName}' failed: {r.FailureReason ?? "unknown error"}."
    };
  }

  /// <summary>Signals that the bound emulator session disappeared while the run was in progress.</summary>
  private sealed class QueueConnectionLostException : Exception {
    public QueueConnectionLostException() { }
    public QueueConnectionLostException(string message) : base(message) { }
    public QueueConnectionLostException(string message, Exception innerException) : base(message, innerException) { }
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
