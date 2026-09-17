using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Emulator.Session;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.EnsureGameRunning;
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
///   EveryStep   — executed after EVERY firing the run performs — at-start, once-per-run, timer,
///                 relative, live and self-reschedule alike — never after itself; not counted.
///                 Feature 060 originally scoped this to OncePerRun steps only, which left the
///                 entry dormant for the whole life of a long-running non-cycling queue once its
///                 once-per-run pass was done (the recovery-guard starvation bug).
///   Timer       — evaluated at each iteration boundary; either an absolute time-of-day (fires at
///                 most once per calendar day) or a relative offset / live schedule (feature 059).
///   BeforeEachRun — executed immediately before the first timed, live or self-rescheduled firing
///                 of each loop iteration (at most once per wake-up), in template order; never before
///                 once-per-run, at-start or every-step executions; not counted (feature 095, #202).
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
  // Exclusive per-emulator ownership (feature 079): claimed before the run launches, released when it
  // ends, so two queues can never drive one screen. When not injected (tests) the service owns a
  // private registry, so claims are still enforced between the queues it starts.
  private readonly IDeviceClaimRegistry _deviceClaims;
  // Foregrounds the game when an idle pause resumes (feature 073). Same handler the
  // ensure-game-running sequence step uses; reused directly here so the scheduler can bring the game
  // back without routing through a watchdog-subject sequence. Null only in tests that omit it.
  private readonly IEnsureGameRunningActionHandler? _ensureGameRunning;
  // Cold-start the queue's LDPlayer instance before binding the device session (feature 074). Reuses
  // the feature-070 ensure-emulator-running handler. Null only in tests that omit it (and when null the
  // pre-session cold-start is skipped, degrading to the pre-074 behavior).
  private readonly IEnsureEmulatorRunningActionHandler? _ensureEmulatorRunning;

  // Confirms the linked game is in front before each firing. Optional: when it is not wired the run
  // behaves exactly as it did before the guard existed.
  private readonly IGameForegroundGuard? _foregroundGuard;

  // Read per firing to pick up a sequence's own watchdog bound. Optional: without it every sequence
  // gets the default bound, as before.
  private readonly GameBot.Domain.Commands.ISequenceRepository? _sequences;

  // Daily-retry bounds (delay and attempt cap) live here; defaults apply when no config is injected.
  private readonly GameBot.Domain.Config.AppConfig _config;

  /// <summary>
  /// Acts on the queue's failure policy when a cycle completes (feature 087). Optional so the many
  /// hand-built test instances of this service keep compiling; null simply means no policy is ever
  /// evaluated, which is also the behaviour for a queue that has not configured one.
  /// </summary>
  private readonly QueueFailurePolicyEvaluator? _failurePolicy;

  // How often a non-cyclic run re-checks pending relative/live timers while waiting for one to become
  // due. Small enough that a firing lands within roughly an iteration interval of the offset, large
  // enough to avoid a busy-wait. (feature 059)
  private static readonly TimeSpan RelativeTimerPollInterval = TimeSpan.FromMilliseconds(250);

  // Per-sequence watchdog: the queue runs one sequence at a time on a single emulator, so a single
  // sequence stuck in a step that never returns (e.g. a WaitForImage whose target never appears, or a
  // lost ADB connection that does not surface as an exception) would otherwise freeze the entire queue
  // indefinitely — starving every timer-scheduled sequence behind it. A firing that exceeds this bound
  // is cancelled and treated as a non-fatal per-sequence failure so the run continues. Generous enough
  // that any legitimate tap/wait sequence completes well within it.
  private static readonly TimeSpan SequenceWatchdogTimeout = TimeSpan.FromMilliseconds(GameBot.Domain.Commands.SequenceTimeLimits.DefaultWatchdogTimeoutMs);

  // Android KEYCODE_HOME. Sent to back the game out to the device home screen during an idle pause
  // (feature 073); HOME leaves the game running in the background, mirroring the go-to-home-screen
  // sequence step.
  private const int AndroidKeyCodeHome = 3;

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
    TimeProvider? timeProvider = null,
    IEnsureGameRunningActionHandler? ensureGameRunning = null,
    IEnsureEmulatorRunningActionHandler? ensureEmulatorRunning = null,
    IDeviceClaimRegistry? deviceClaims = null,
    IGameForegroundGuard? foregroundGuard = null,
    GameBot.Domain.Commands.ISequenceRepository? sequences = null,
    GameBot.Domain.Config.AppConfig? config = null,
    QueueFailurePolicyEvaluator? failurePolicy = null) {
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
    _ensureGameRunning = ensureGameRunning;
    _ensureEmulatorRunning = ensureEmulatorRunning;
    _deviceClaims = deviceClaims ?? new DeviceClaimRegistry();
    _foregroundGuard = foregroundGuard;
    _sequences = sequences;
    _config = config ?? new GameBot.Domain.Config.AppConfig();
    _failurePolicy = failurePolicy;
  }

  public bool IsRunning(string queueId) => _registry.IsRunning(queueId);

  /// <summary>
  /// Decides what a cancelled run should report as its stop reason (feature 087).
  /// <para>
  /// An operator stop and a failure-policy stop both cancel the run's single
  /// <see cref="QueueRunHandle.Cts"/>, so the cancellation itself carries no attribution. The
  /// handle's marker, set by the evaluator before it cancels, is the only distinguishing signal —
  /// and the distinction matters: reporting a policy stop as <c>StoppedManually</c> would tell an
  /// operator a person halted production when nobody did.
  /// </para>
  /// </summary>
  private static QueueStopReason AttributeCancellation(QueueRunHandle handle) =>
    handle.StopRequestedByPolicy
      ? QueueStopReason.StoppedByFailurePolicy
      : QueueStopReason.StoppedManually;

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
    var handle = new QueueRunHandle {
      QueueId = queueId,
      Cts = cts,
      CycleExecution = queue.CycleExecution,
      DeviceSerial = queue.EmulatorSerial
    };
    if (!_registry.TryAdd(queueId, handle)) {
      cts.Dispose();
      return QueueStartOutcome.AlreadyRunning;
    }

    // Feature 079 (FR-008/FR-009/FR-012): one run per device. Claim before launching, so a refusal
    // leaves the queue Stopped with no run and no residue in the run registry. TryClaim is atomic, so
    // two simultaneous starts for one serial cannot both win. Released in RunAsync's finally, which
    // covers completion, manual stop, failure, cancellation and host shutdown (FR-011).
    if (!_deviceClaims.TryClaim(queue.EmulatorSerial, queueId, queue.Name)) {
      _registry.Remove(queueId, out _);
      cts.Dispose();
      return QueueStartOutcome.DeviceInUse;
    }

    // Resolve the linked template once and materialize its entries into the runtime store so the
    // entries shown by GET match what the run executes. The run reuses this same snapshot; the
    // display layer instead reads the runtime store, and auto-load on display is suppressed once
    // the queue is Running (see QueuesEndpoints.MaybeAutoLoadAsync). Without this, a queue started
    // before it was ever displayed reports zero entries despite a populated linked template.
    QueueTemplate? template;
    try {
      template = string.IsNullOrEmpty(queue.LinkedTemplateId)
        ? null
        : await _templates.GetAsync(queue.LinkedTemplateId).ConfigureAwait(false);
    }
    catch {
      // The run never launched, so RunAsync's finally will not run: undo the claim and the registry
      // entry here rather than leaking the device until the service restarts (feature 079, FR-011).
      _deviceClaims.Release(queue.EmulatorSerial, queueId);
      _registry.Remove(queueId, out _);
      cts.Dispose();
      throw;
    }
    if (template is not null) {
      // Materialize ALL entries (including disabled ones) into the runtime store: the template
      // editor renders from these runtime entries and merges each entry's schedule/enabled state
      // from the template detail BY POSITION, so disabled entries must stay present and in order
      // for the operator to see and re-enable them. Execution excludes disabled entries in RunAsync,
      // where the template is read directly (077).
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
        // Once-per-run and timer partitions carry each entry's index in `allEntries`: that index is the
        // stable key the run's QueueRunSchedule records consumed work under, so the monitor can project
        // exactly what is left instead of re-deriving an idealized plan from the template.
        // Disabled entries (Enabled == false) are excluded once here, so every schedule partition
        // (AtQueueStart/OncePerRun/EveryStep/Timer) and the monitor projection all skip them (077).
        var allEntries = template.Entries.Where(e => e.Enabled).ToList();
        var indexed = allEntries.Select((Entry, Index) => (Entry, Index)).ToList();

        // Feature 078: the outermost parameter layer is derived from the queue's own configuration, so
        // three queues that already differ by emulator serial drive one shared sequence with no extra
        // setup. Each firing layers its entry's values on top (FR-010, FR-012).
        var queueScope = ParameterScope.FromQueue(queue);
        ParameterScope EntryScope(QueueTemplateEntry entry) =>
            entry.ParameterValues.Count > 0
              ? queueScope.Child(ParameterScopeLayers.Entry, entry.ParameterValues, null)
              : queueScope;
        var atQueueStartEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.AtQueueStart).ToList();
        var oncePerRunEntries = indexed.Where(x => x.Entry.ScheduleType == ScheduleType.OncePerRun).ToList();
        var everyStepEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.EveryStep).ToList();
        var beforeEachRunEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.BeforeEachRun).ToList();
        var timerEntries = indexed.Where(x => x.Entry.ScheduleType == ScheduleType.Timer).ToList();

        // 1.5 Feature 074: when the queue is configured with an emulator instance identifier, bring that
        // instance up BEFORE binding the device session, so a queue can self-start from a backend-only
        // cold state (the emulator being closed would otherwise fail CreateSession below). A genuine
        // failure (recovery timeout / instance not found) fails the run here and skips session creation;
        // success or a neutral unsupported outcome proceeds exactly as today.
        var emulatorFailure = await EnsureEmulatorBeforeSessionAsync(queue, ct).ConfigureAwait(false);
        if (emulatorFailure is not null) {
          reason = QueueStopReason.Failure;
          failureReason = emulatorFailure;
        }
        else {
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
        }

        // 3. Run sequences in order, respecting schedule types, optionally cycling.
        if (sessionId is not null) {
          try {
            var index = 0;

            // Every-step pass: run each template EveryStep entry, then each self-reschedule EveryStep
            // injection, once. Called after EVERY firing the run performs (at-start, once-per-run,
            // time-of-day, relative, live and self-reschedule), which is what makes an EveryStep entry
            // usable as a recovery guard on a long-lived non-cycling queue: scoping it to the
            // once-per-run pass (feature 060, FR-005) left it dormant from the moment that pass ended.
            //
            // Never called from inside itself, so an EveryStep firing still cannot trigger another
            // round (FR-006 loop-safety is preserved), and these executions still do not count toward
            // `executed` (FR-015) — only toward `failed` when one fails, which stays non-fatal.
            var everyStepRanThisIteration = false;
            async Task RunEveryStepPassAsync() {
              if (everyStepEntries.Count == 0 && handle.EveryStepInjections.IsEmpty) return;
              everyStepRanThisIteration = true;

              foreach (var esEntry in everyStepEntries) {
                ct.ThrowIfCancellationRequested();
                if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                var esOk = await RunOneSequenceAsync(esEntry.SequenceId, rootId, ++index, sessionId, queue.Id, EntryScope(esEntry), ct).ConfigureAwait(false);
                if (!esOk) failed++;
                handle.Cycles.RecordEntry(esEntry.SequenceId, esOk);
              }

              // Self-reschedule EveryStep injections (feature 065, FR-008). Snapshot first so a
              // firing's own re-registration cannot grow the pass (loop-safe).
              foreach (var injection in handle.EveryStepInjections.Values.ToList()) {
                ct.ThrowIfCancellationRequested();
                if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                var injOk = await RunOneSequenceAsync(injection.SequenceId, rootId, ++index, sessionId, queue.Id, injection.Scope ?? queueScope, ct, injection.Id).ConfigureAwait(false);
                if (!injOk) failed++;
                handle.Cycles.RecordEntry(injection.SequenceId, injOk);
              }
            }

            // Before-each-run pass (feature 095, #202): the mirror of the every-step pass. Awaited
            // immediately before every timed, live or self-rescheduled firing, but runs at most once per
            // loop iteration — several firings due at one wake-up share a single pass, which precedes
            // the first of them. Accounting matches the every-step pass: not counted toward `executed`,
            // a failure counts toward `failed` and is non-fatal, and the triggering firing still runs.
            // It never runs the every-step pass or itself, so it cannot loop.
            var beforeEachRunRanThisIteration = false;
            async Task RunBeforeEachRunPassAsync() {
              if (beforeEachRunRanThisIteration || beforeEachRunEntries.Count == 0) return;
              beforeEachRunRanThisIteration = true;

              foreach (var berEntry in beforeEachRunEntries) {
                ct.ThrowIfCancellationRequested();
                if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                var berOk = await RunOneSequenceAsync(berEntry.SequenceId, rootId, ++index, sessionId, queue.Id, EntryScope(berEntry), ct).ConfigureAwait(false);
                if (!berOk) failed++;
                handle.Cycles.RecordEntry(berEntry.SequenceId, berOk);
              }
            }

            // (0) At-queue-start pre-pass (feature 060, FR-003/FR-004/FR-007/FR-014/FR-015).
            // Run every at-queue-start entry once, in template order, BEFORE any timer evaluation
            // and before the first OncePerRun step. Runs once per run (outside the do/while, so it
            // never repeats on a cycling queue). Each firing COUNTS toward `executed`; a failure is
            // non-fatal (recorded in `failed`, run continues), consistent with OncePerRun handling.
            foreach (var startEntry in atQueueStartEntries) {
              ct.ThrowIfCancellationRequested();
              if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
              var startOk = await RunOneSequenceAsync(startEntry.SequenceId, rootId, ++index, sessionId, queue.Id, EntryScope(startEntry), ct).ConfigureAwait(false);
              executed++;
              if (!startOk) failed++;
              await RunEveryStepPassAsync().ConfigureAwait(false);
            }

            // Per-run scheduling state (FR-003/FR-012, feature 059 FR-005): the relative-offset anchor
            // plus every "already consumed" register — time-of-day timers fired today, relative timers
            // fired this run, once-per-run steps completed this cycle, and whether the once-per-run
            // pass has run at all (a non-cyclic run does it once; later loop iterations exist only to
            // wait for pending relative/live timers and must not re-run those steps). Lives outside the
            // do-while so it survives cycles, and is published on the handle so the monitor projects the
            // run's real remaining plan rather than an idealized one (feature 072/073 fix).
            var runStartedAt = _timeProvider.GetLocalNow();
            handle.RunStartedAt = runStartedAt;
            var schedule = new QueueRunSchedule(allEntries, runStartedAt, queue.CycleExecution);
            handle.Schedule = schedule;

            // True while a relative-offset timer (template) or a live schedule is still pending and
            // could yet fire — keeps a non-cyclic run alive until its scheduled firings land.
            bool HasPendingRelativeOrLive() {
              if (schedule.HasUnfiredRelativeTimers) return true;
              if (!handle.PendingLiveSchedules.IsEmpty) return true;
              // An armed daily retry is pending work too: without this the run could break out of the
              // loop between a failed firing and its retry, silently losing the retry it just armed.
              if (schedule.HasPendingDailyRetries) return true;
              // feature 065: a self-reschedule Timer firing not yet due keeps a non-cyclic run alive
              // until it lands (or the run is stopped), exactly like a relative/live schedule.
              return handle.HasPendingTimerFirings;
            }

            // The loop also runs when the at-queue-start pass booked work only the loop drains
            // (self-reschedule / live schedules): otherwise an AtQueueStart-only template would take the
            // empty-template branch and silently drop those bookings (feature 092, #198).
            if (oncePerRunEntries.Count > 0 || everyStepEntries.Count > 0 || timerEntries.Count > 0
                || handle.HasPendingSelfRescheduleWork) {
              do {
                ct.ThrowIfCancellationRequested();
                // Failure-policy pause gate (feature 087): hold here while the run is parked. This
                // sits BEFORE every due-ness evaluation below, which is what makes resume free —
                // a firing that came due during the pause is simply still due (FR-018a).
                await handle.WaitIfPausedAsync(ct).ConfigureAwait(false);
                everyStepRanThisIteration = false;
                beforeEachRunRanThisIteration = false;

                // Cycle ledger (feature 086): open the cycle this iteration will fill. Idempotent, so
                // a non-cyclic run's trailing timer-poll iterations reuse the cycle they never
                // complete — and it is therefore never published.
                handle.Cycles.EnsureOpen(_timeProvider.GetLocalNow());
                // Every sequence firing advances `index`, so an unchanged value at the end of the
                // iteration means nothing ran (feature 093, #200).
                var indexAtIterationStart = index;

                // (a0) Self-reschedule AtQueueStart firings (feature 065, FR-009): entries queued
                // during the previous cycle fire at the top of the next cycle, before timers and the
                // once-per-run pass. Count toward executed; a failed firing is non-fatal.
                while (handle.PendingNextCycleStart.TryDequeue(out var nextCycleEntry)) {
                  await RunBeforeEachRunPassAsync().ConfigureAwait(false);
                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  var nextOk = await RunOneSequenceAsync(nextCycleEntry.SequenceId, rootId, ++index, sessionId, queue.Id, nextCycleEntry.Scope ?? queueScope, ct, nextCycleEntry.Id).ConfigureAwait(false);
                  executed++;
                  if (!nextOk) failed++;
                  handle.Cycles.RecordEntry(nextCycleEntry.SequenceId, nextOk);
                  await RunEveryStepPassAsync().ConfigureAwait(false);
                }

                // (a) Evaluate timer entries at iteration boundary (FR-011/FR-012/FR-016).
                foreach (var (timerEntry, timerIndex) in timerEntries) {
                  if (timerEntry.TimerTimeOfDay is null) continue;

                  var localNow = _timeProvider.GetLocalNow();
                  var today = DateOnly.FromDateTime(localNow.DateTime);
                  var now = TimeOnly.FromDateTime(localNow.DateTime);
                  if (now >= timerEntry.TimerTimeOfDay.Value && !schedule.TimeOfDayFiredOn(timerIndex, today)) {
                    await RunBeforeEachRunPassAsync().ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                    var timerOk = await RunOneSequenceAsync(timerEntry.SequenceId, rootId, ++index, sessionId, queue.Id, EntryScope(timerEntry), ct).ConfigureAwait(false);
                    if (!timerOk) failed++;
                    // Timer executions do not count toward `executed` (SC-002 analogue for timers)
                    schedule.MarkTimeOfDayFired(timerIndex, today);
                    ArmOrClearDailyRetry(schedule, timerIndex, timerEntry.SequenceId, timerOk, attempt: 0, localNow);
                    await RunEveryStepPassAsync().ConfigureAwait(false);
                  }
                }

                // (a1b) Re-fire time-of-day entries whose sequence failed (bounded by
                // QueueDailyRetryMaxAttempts). A daily slot fires once per calendar day, so without
                // this one flaky firing silently costs the whole day's task.
                foreach (var (retryIndex, attempt) in schedule.DueDailyRetries(_timeProvider.GetLocalNow())) {
                  var retryEntry = schedule.Entries[retryIndex];
                  await RunBeforeEachRunPassAsync().ConfigureAwait(false);
                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  QueueExecutionLog.DailyRetryFiring(_logger, retryEntry.SequenceId, attempt);
                  var retryOk = await RunOneSequenceAsync(retryEntry.SequenceId, rootId, ++index, sessionId, queue.Id, EntryScope(retryEntry), ct).ConfigureAwait(false);
                  if (!retryOk) failed++;
                  ArmOrClearDailyRetry(schedule, retryIndex, retryEntry.SequenceId, retryOk, attempt, _timeProvider.GetLocalNow());
                  await RunEveryStepPassAsync().ConfigureAwait(false);
                }

                // (a2) Evaluate relative-offset timers at the iteration boundary (feature 059).
                // Fire once per run when elapsed-since-run-start >= offset (FR-005). Relative firings
                // COUNT toward `executed` (FR-016a), unlike time-of-day timers. A failed firing is
                // non-fatal: recorded in `failed`, run continues (FR-016).
                var elapsedSinceStart = _timeProvider.GetLocalNow() - runStartedAt;
                foreach (var (relEntry, relIndex) in timerEntries) {
                  if (relEntry.TimerRelativeOffset is not { } relOffset) continue;
                  if (schedule.RelativeFired(relIndex)) continue;
                  if (elapsedSinceStart < relOffset) continue;

                  await RunBeforeEachRunPassAsync().ConfigureAwait(false);
                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  var relOk = await RunOneSequenceAsync(relEntry.SequenceId, rootId, ++index, sessionId, queue.Id, EntryScope(relEntry), ct).ConfigureAwait(false);
                  executed++;
                  if (!relOk) failed++;
                  schedule.MarkRelativeFired(relIndex);
                  await RunEveryStepPassAsync().ConfigureAwait(false);
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
                  await RunBeforeEachRunPassAsync().ConfigureAwait(false);
                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  var liveOk = await RunOneSequenceAsync(due, rootId, ++index, sessionId, queue.Id, queueScope, ct).ConfigureAwait(false);
                  executed++;
                  if (!liveOk) failed++;
                  handle.Cycles.RecordEntry(due, liveOk);
                  await RunEveryStepPassAsync().ConfigureAwait(false);
                }

                // (a4) Self-reschedule Timer firings (feature 065, FR-005/FR-006): fire those whose
                // resolved instant is at/before now, once each, then remove. Count toward executed; a
                // failed firing is non-fatal. Entries never due before the run ends are discarded with
                // the handle and never fail the run (FR-015).
                foreach (var timerFiring in handle.DrainDueTimerFirings(_timeProvider.GetLocalNow())) {
                  await RunBeforeEachRunPassAsync().ConfigureAwait(false);
                  ct.ThrowIfCancellationRequested();
                  if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                  var srTimerOk = await RunOneSequenceAsync(timerFiring.SequenceId, rootId, ++index, sessionId, queue.Id, timerFiring.Scope ?? queueScope, ct, timerFiring.Id).ConfigureAwait(false);
                  executed++;
                  if (!srTimerOk) failed++;
                  handle.Cycles.RecordEntry(timerFiring.SequenceId, srTimerOk);
                  await RunEveryStepPassAsync().ConfigureAwait(false);
                }

                // (b) OncePerRun steps, each followed by all EveryStep sequences (FR-006/FR-007/FR-016).
                // A cycling run executes these every cycle; a non-cyclic run executes them once, so the
                // relative/live timer-wait passes below never re-run the once-per-run steps. A cycling
                // iteration where neither this pass nor any firing above runs a sequence is not a cycle
                // and is not counted (feature 093, #200) — it waits below instead.
                var cycleHasWork = oncePerRunEntries.Count > 0 || everyStepEntries.Count > 0
                  || !handle.PendingOncePerRun.IsEmpty || index != indexAtIterationStart;
                if (queue.CycleExecution ? cycleHasWork : !schedule.OncePerRunPassDone) {
                  schedule.BeginCycle();
                  if (oncePerRunEntries.Count > 0) {
                    foreach (var (entry, entryIndex) in oncePerRunEntries) {
                      ct.ThrowIfCancellationRequested();
                      if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                      handle.Cycles.SetCurrentEntryIndex(entryIndex);
                      var ok = await RunOneSequenceAsync(entry.SequenceId, rootId, ++index, sessionId, queue.Id, EntryScope(entry), ct).ConfigureAwait(false);
                      executed++;
                      if (!ok) failed++;
                      handle.Cycles.RecordEntry(entry.SequenceId, ok);
                      schedule.MarkOncePerRunCompleted(entryIndex);

                      // Run every-step sequences after each OncePerRun step (FR-006).
                      await RunEveryStepPassAsync().ConfigureAwait(false);
                      handle.Cycles.ClearCurrentEntryIndex();
                    }
                  }
                  else if (everyStepEntries.Count > 0 && !everyStepRanThisIteration) {
                    // FR-009: no OncePerRun entries — EveryStep still runs at least once per cycle,
                    // unless a firing earlier in this iteration already triggered a pass.
                    await RunEveryStepPassAsync().ConfigureAwait(false);
                  }

                  // OncePerRun self-reschedule firings (feature 065, FR-007), and the non-cycling
                  // AtQueueStart fallback: drain a snapshot of those queued this cycle and fire each
                  // before the cycle ends. Count toward executed; failures are non-fatal. Snapshotting
                  // bounds a single drain so an always-true self-reschedule cannot spin within one cycle
                  // (further generations fire next cycle / are abandoned at run end — FR-015).
                  var oncePerRunReschedules = new List<SelfRescheduleEntry>();
                  while (handle.PendingOncePerRun.TryDequeue(out var oprEntry)) oncePerRunReschedules.Add(oprEntry);
                  foreach (var oprFiring in oncePerRunReschedules) {
                    await RunBeforeEachRunPassAsync().ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
                    var oprOk = await RunOneSequenceAsync(oprFiring.SequenceId, rootId, ++index, sessionId, queue.Id, oprFiring.Scope ?? queueScope, ct, oprFiring.Id).ConfigureAwait(false);
                    executed++;
                    if (!oprOk) failed++;
                    handle.Cycles.RecordEntry(oprFiring.SequenceId, oprOk);
                    await RunEveryStepPassAsync().ConfigureAwait(false);
                  }

                  schedule.MarkOncePerRunPassDone();
                  cycles++;
                  // Publish the cycle exactly when the engine counts one (feature 086).
                  handle.Cycles.CompleteOpen(_timeProvider.GetLocalNow());
                  // Act on the queue's failure policy, if it has one (feature 087). Never throws,
                  // never awaits delivery, and returns immediately when no policy is configured.
                  _failurePolicy?.OnCycleCompleted(queue, handle);
                }

                // A cycling run that ran something loops immediately (existing behavior). One that ran
                // nothing falls through to the wait below instead of spinning empty cycles as fast as
                // the host allows, and never ends on its own (feature 093, #200). A non-cyclic run
                // breaks once its once-per-run steps are done UNLESS a relative-offset timer or live
                // schedule is still pending — in which case it stays alive, polling, until those fire or
                // it is stopped. Without this a non-cyclic run would finish instantly and a "+10s"
                // relative timer (or live schedule) would never become due (feature 059 fix).
                if (queue.CycleExecution) {
                  if (index != indexAtIterationStart) continue;
                  handle.Cycles.DiscardOpenIfEmpty();
                }
                else if (!HasPendingRelativeOrLive()) {
                  break;
                }

                // Idle-pause (feature 073): when the queue opts in and the gap to the next firing
                // exceeds the configured idle-detection threshold, back the game out to the home
                // screen for the gap and foreground it when the firing is due — instead of a bare
                // poll delay. Watchdog-exempt and log-silent because it runs inline here, never via
                // RunOneSequenceAsync. Otherwise fall back to the existing one-tick poll delay.
                var pollNow = _timeProvider.GetLocalNow();
                var nextDue = schedule.ComputeNextDue(handle, pollNow);
                var thresholdSeconds = Math.Max(1, queue.IdleThresholdSeconds);
                if (queue.PauseWhenIdle
                    && nextDue is { } dueAt
                    && dueAt - pollNow > TimeSpan.FromSeconds(thresholdSeconds)) {
                  await IdlePauseHoldAsync(dueAt, sessionId!, handle, now => schedule.ComputeNextDue(handle, now), ct).ConfigureAwait(false);
                }
                else {
                  await Task.Delay(RelativeTimerPollInterval, ct).ConfigureAwait(false);
                }
              } while (true);
            }
            else {
              // Empty template: a full pass with no work; never busy-loop when cycling (FR-017).
              cycles = 1;
              // Still a completed cycle (feature 086), so an idle-but-alive queue stays
              // distinguishable from a stalled one.
              handle.Cycles.RecordEmptyCycle(_timeProvider.GetLocalNow());
            }
            reason = QueueStopReason.CompletedFullRun;
          }
          catch (QueueConnectionLostException) {
            reason = QueueStopReason.Failure;
            failureReason = $"emulator connection lost mid-run ('{queue.EmulatorSerial}')";
          }
          catch (OperationCanceledException) {
            // An operator stop and a failure-policy stop cancel the same token, so the marker on
            // the handle is the only thing that tells them apart (feature 087, FR-017).
            reason = AttributeCancellation(handle);
          }
        }
      }
    }
    catch (OperationCanceledException) {
      reason = AttributeCancellation(handle);
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
        // The newest segment, not the one this run opened: a long run may have rotated since, and the
        // earlier segments were already closed out by the rotation itself.
        var finalizeRootId = handle.RootExecutionId ?? rootId;
        await _log.LogQueueFinalizeAsync(finalizeRootId, queue.Id, queue.Name, finalStatus, BuildSummary(queue.Name, result), ct: CancellationToken.None).ConfigureAwait(false);
      }
      catch (Exception ex) { QueueExecutionLog.FinalizeFailed(_logger, queue.Id, ex); }

      _runtime.SetStatus(queue.Id, QueueExecutionStatus.Stopped);
      _registry.Remove(queue.Id, out _);
      // Feature 079 (FR-011): the device becomes claimable again however this run ended — completed,
      // stopped, failed, cancelled, or torn down by host shutdown.
      _deviceClaims.Release(queue.EmulatorSerial, queue.Id);
      handle.Cts.Dispose();
    }
  }

  /// <summary>
  /// Runs one sequence as a child of the queue run. Per-sequence failures are non-fatal (FR-008):
  /// returns false on a failed/unresolved sequence so the run can continue.
  /// </summary>
  /// <summary>
  /// Feature 074: pre-session emulator cold-start. Returns <c>null</c> when no emulator work is needed
  /// (no instance identifier configured, or no handler injected) or when the instance ends up healthy
  /// (already-healthy / started / restarted) or the host cannot drive the emulator (neutral
  /// unsupported). Returns an actionable failure reason string ONLY when the instance genuinely could
  /// not be brought up (recovery timeout / instance not found) — in which case the caller must not
  /// create the session and fails the run. Runs before <see cref="ISessionManager.CreateSession"/> so a
  /// closed emulator no longer fails session creation.
  /// </summary>
  private async Task<string?> EnsureEmulatorBeforeSessionAsync(ExecutionQueue queue, CancellationToken ct) {
    if (_ensureEmulatorRunning is null) return null;
    if (string.IsNullOrWhiteSpace(queue.EmulatorInstanceName) && queue.EmulatorInstanceIndex is null) return null;

    var args = new GameBot.Domain.Actions.EnsureEmulatorRunningArgs {
      InstanceName = string.IsNullOrWhiteSpace(queue.EmulatorInstanceName) ? null : queue.EmulatorInstanceName,
      InstanceIndex = queue.EmulatorInstanceIndex,
      AdbSerial = queue.EmulatorSerial
    };
    var target = queue.EmulatorInstanceName ?? $"#{queue.EmulatorInstanceIndex}";
    var result = await _ensureEmulatorRunning.ExecuteAsync(args, ct).ConfigureAwait(false);
    if (result.IsSuccess || result.IsUnsupported) {
      QueueExecutionLog.PreSessionEmulatorEnsured(_logger, queue.Id, target, result.ReasonCode);
      return null;
    }
    QueueExecutionLog.PreSessionEmulatorFailed(_logger, queue.Id, target, result.ReasonCode);
    return $"emulator instance ('{target}') could not be started: {result.ReasonCode}";
  }

  /// <summary>
  /// How long one execution-log run segment may stay open. A run still going after this is closed
  /// and continued in a fresh segment, so a queue left running for weeks cannot accumulate one
  /// unbounded run in the log.
  /// </summary>
  private static readonly TimeSpan RunSegmentMaxAge = TimeSpan.FromHours(24);

  /// <summary>
  /// Closes the current execution-log run segment and opens a continuation when the segment has been
  /// open longer than <see cref="RunSegmentMaxAge"/>, returning the root id later firings must use.
  /// Called only at a firing boundary (see <see cref="RunOneSequenceAsync"/>), so a rotation can never
  /// split one sequence's execution across two segments.
  /// </summary>
  private async Task<string> RotateRootIfDueAsync(string rootId, string queueId, CancellationToken ct) {
    // A stop request aborts the upcoming firing anyway; rotating now would only strand a fresh,
    // empty segment as the run's last word.
    if (ct.IsCancellationRequested) return rootId;

    try {
      // The segment's own start time is its age — no separate clock to keep in sync, and it survives
      // a service restart mid-run because it is read back from the persisted entry.
      var current = await _log.GetAsync(rootId, CancellationToken.None).ConfigureAwait(false);
      if (current is null) return rootId;
      if (_timeProvider.GetUtcNow() - current.TimestampUtc <= RunSegmentMaxAge) return rootId;

      // Written with CancellationToken.None so a stop landing mid-rotation cannot leave the old
      // segment closed with no continuation to point at.
      var continuationId = await _log
        .LogQueueRotateAsync(rootId, queueId, current.ObjectRef.DisplayNameSnapshot, CancellationToken.None)
        .ConfigureAwait(false);
      if (_registry.TryGet(queueId, out var rotatedHandle)) {
        rotatedHandle.RootExecutionId = continuationId;
      }
      QueueExecutionLog.ExecutionLogRotated(_logger, queueId, rootId, continuationId);
      return continuationId;
    }
    catch (Exception ex) {
      // Tidier logs are never worth losing a run over: keep firing into the current segment and let
      // the next boundary try again.
      QueueExecutionLog.ExecutionLogRotationFailed(_logger, queueId, ex);
      return rootId;
    }
  }

  private async Task<bool> RunOneSequenceAsync(string sequenceId, string rootId, int index, string sessionId, string queueId, ParameterScope scope, CancellationToken ct, string? selfRescheduleOriginActionId = null) {
    // Watchdog: cancel a firing that overruns its bound so one stuck sequence cannot freeze the whole
    // queue. Linked to ct so a real stop request still cancels immediately.
    // A sequence may raise its own bound (CommandSequence.WatchdogTimeoutMs). Some legitimately need
    // longer than the default — one that waits out a scripted in-game animation such as an auto-battle
    // can exceed it on every single run, so the default would not protect that sequence but abort it
    // every time. Anything without an override keeps the default exactly as before.
    var watchdogTimeout = await ResolveWatchdogTimeoutAsync(sequenceId).ConfigureAwait(false);
    // The bound gets its own timer token (feature 094) so the sequence's log entry can tell "ran out of
    // time" from "the run was stopped" — a single linked source carrying both would erase that.
    using var watchdogTimer = new CancellationTokenSource(watchdogTimeout);
    using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct, watchdogTimer.Token);
    using var timeLimitScope = SequenceTimeLimitScope.Push((int)watchdogTimeout.TotalMilliseconds, watchdogTimer.Token, ct);
    // Sequence-level "now" tracking for the live monitor (feature 072): every firing — at-start,
    // once-per-run, every-step, timer, relative, live, self-reschedule — flows through here, so
    // set the current sequence at the top and clear it in the finally. This is purely observational
    // and does not change scheduling behavior.
    // Feature 079: the ambient device context for this firing is established by
    // SequenceExecutionService.ExecuteAsync from the sessionId passed below, and covers everything the
    // firing starts (nested sequences, commands, loops, image/text conditions). Pushing it again here
    // would be redundant, so the run loop deliberately does not.
    var trackedHandle = _registry.TryGet(queueId, out var handle) ? handle : null;
    // Rotation decision, made before any of this firing's work starts — the run loop's callers hold a
    // root id captured at run start, so the handle (updated in place on rotation) is the live source.
    var activeRootId = await RotateRootIfDueAsync(trackedHandle?.RootExecutionId ?? rootId, queueId, ct)
      .ConfigureAwait(false);
    trackedHandle?.SetCurrentSequence(sequenceId, _timeProvider.GetLocalNow());
    try {
      // Foreground guard: a queue run holds one emulator for hours, and anything that pushes the
      // game out of the foreground in that window (a recovery loop pressing BACK off the game's top
      // screen, a crash, someone touching the emulator) makes every later image detection read the
      // device launcher instead of the game. Without this, the run keeps firing sequences that can
      // only fail — the game's launch step runs at queue start and never again — so the queue reports
      // "Running" indefinitely while achieving nothing. Confirming the foreground here means a
      // dropped-out game costs at most one firing instead of the rest of the run.
      // Best-effort by design: a guard failure never fails the firing, exactly like the idle-pause
      // foreground (FR-011). Runs under the watchdog token so a stop request cancels it promptly.
      if (_foregroundGuard is not null) {
        try {
          var guard = await _foregroundGuard.EnsureForegroundAsync(sessionId, watchdog.Token).ConfigureAwait(false);
          if (guard.Recovered) {
            QueueExecutionLog.ForegroundGuardRecovered(_logger, queueId, sequenceId);
          }
          else if (guard.Failed) {
            QueueExecutionLog.ForegroundGuardFailed(_logger, queueId, sequenceId, guard.ReasonCode);
          }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { /* watchdog — fall through and let the firing try anyway */ }
        catch (Exception ex) { QueueExecutionLog.ForegroundGuardFaulted(_logger, queueId, ex); }
      }

      var parentContext = new ExecutionLogContext {
        ParentExecutionId = activeRootId,
        RootExecutionId = activeRootId,
        Depth = 1,
        SequenceIndex = index,
        // Mark this firing as queue-originated so a self-reschedule action can target this run
        // (FR-018); also carry the originating action id for attribution of self-reschedule firings.
        OriginatingQueueId = queueId,
        SelfRescheduleOriginActionId = selfRescheduleOriginActionId
      };
      var res = await _sequenceExecution.ExecuteAsync(sequenceId, sessionId, parentContext, scope, ct: watchdog.Token).ConfigureAwait(false);
      return string.Equals(res.Status, "Succeeded", StringComparison.OrdinalIgnoreCase);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
      throw; // a stop request must propagate to abort the run
    }
    catch (OperationCanceledException) {
      // Watchdog fired: the sequence overran its bound. Non-fatal — record it and let the run continue
      // so the timeout releases the queue instead of hanging it (FR-008/008b analogue).
      QueueExecutionLog.SequenceWatchdogTimedOut(_logger, sequenceId, (int)watchdogTimeout.TotalSeconds);
      return false;
    }
    catch (Exception ex) {
      // Unexpected per-sequence error (e.g. a stale/unresolved reference): non-fatal (FR-008/008b).
      QueueExecutionLog.SequenceFaulted(_logger, sequenceId, ex);
      return false;
    }
    finally {
      trackedHandle?.ClearCurrentSequence();
    }
  }

  /// <summary>
  /// Resolves the watchdog bound for one firing: the sequence's own <c>WatchdogTimeoutMs</c> when it
  /// sets one, otherwise <see cref="SequenceWatchdogTimeout"/>. A missing repository, a missing
  /// sequence, a non-positive override, or a lookup failure all fall back to the default — the bound
  /// is a safety net, so it must never be the thing that breaks a run.
  /// </summary>
  /// <summary>
  /// Settles the retry state of one time-of-day firing: a success (or a run with retries disabled)
  /// clears it, a failure with attempts left arms the next one, and a failure that exhausts them gives
  /// up until the entry's next daily slot.
  /// </summary>
  private void ArmOrClearDailyRetry(QueueRunSchedule schedule, int entryIndex, string sequenceId, bool succeeded, int attempt, DateTimeOffset now) {
    var maxAttempts = Math.Max(0, _config.QueueDailyRetryMaxAttempts);
    if (succeeded || maxAttempts == 0) {
      schedule.ClearDailyRetry(entryIndex);
      return;
    }

    var nextAttempt = attempt + 1;
    if (nextAttempt > maxAttempts) {
      schedule.ClearDailyRetry(entryIndex);
      QueueExecutionLog.DailyRetryExhausted(_logger, sequenceId, maxAttempts);
      return;
    }

    var delay = TimeSpan.FromMilliseconds(Math.Max(1, _config.QueueDailyRetryDelayMs));
    schedule.ArmDailyRetry(entryIndex, now + delay, nextAttempt);
    QueueExecutionLog.DailyRetryArmed(_logger, sequenceId, nextAttempt, maxAttempts, (int)delay.TotalMinutes);
  }

  private async Task<TimeSpan> ResolveWatchdogTimeoutAsync(string sequenceId) {
    if (_sequences is null) return SequenceWatchdogTimeout;
    try {
      var sequence = await _sequences.GetAsync(sequenceId).ConfigureAwait(false);
      return TimeSpan.FromMilliseconds(GameBot.Domain.Commands.SequenceTimeLimits.Resolve(sequence?.WatchdogTimeoutMs));
    }
    catch (Exception ex) {
      QueueExecutionLog.WatchdogTimeoutLookupFailed(_logger, sequenceId, ex);
    }
    return SequenceWatchdogTimeout;
  }

  /// <summary>
  /// Holds an idle pause (feature 073): backs the game out to the device home screen, marks the run
  /// idle-paused until <paramref name="resumeAt"/> (surfaced by the monitor), and waits — recomputing
  /// the next-due firing each poll tick so an earlier-arriving firing shortens the pause — then brings
  /// the game back to the foreground. Runs inline (NOT via <see cref="RunOneSequenceAsync"/>), so it is
  /// exempt from the per-sequence watchdog (FR-006) and writes no execution-log entries (FR-007a). Both
  /// the background and foreground are best-effort/non-fatal (FR-011); a stop request cancels the hold
  /// within one poll interval (FR-012) and never foregrounds. The idle-pause state is always cleared in
  /// the finally so a cancellation cannot leave the run marked paused (T010).
  /// </summary>
  private async Task IdlePauseHoldAsync(
      DateTimeOffset resumeAt,
      string sessionId,
      QueueRunHandle handle,
      Func<DateTimeOffset, DateTimeOffset?> computeNextDue,
      CancellationToken ct) {
    handle.EnterIdlePause(resumeAt);
    try {
      // Background the game (best-effort): send HOME once. A failure is non-fatal — the run keeps
      // going and scheduled tasks still fire.
      try {
        var home = new InputAction("key", new Dictionary<string, object> { ["keyCode"] = AndroidKeyCodeHome });
        await _sessions.SendInputsAsync(sessionId, new[] { home }, ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) { throw; }
      catch (Exception ex) { QueueExecutionLog.IdlePauseBackgroundFailed(_logger, handle.QueueId, ex); }

      // Hold until the next firing is due (or an earlier one arrives), or the run is stopped.
      while (true) {
        ct.ThrowIfCancellationRequested();
        var now = _timeProvider.GetLocalNow();
        var nextDue = computeNextDue(now);
        if (nextDue is not { } due || due <= now) break;
        // Reflect an earlier-arriving firing in the monitor's resume time.
        handle.EnterIdlePause(due);
        await Task.Delay(RelativeTimerPollInterval, ct).ConfigureAwait(false);
      }

      // Resume: foreground the game (best-effort). A failure is non-fatal — the due sequence's own
      // connect/recovery steps handle a game that is not in front (FR-011).
      try {
        if (_ensureGameRunning is not null) {
          await _ensureGameRunning.ExecuteAsync(sessionId, ct).ConfigureAwait(false);
        }
      }
      catch (OperationCanceledException) { throw; }
      catch (Exception ex) { QueueExecutionLog.IdlePauseForegroundFailed(_logger, handle.QueueId, ex); }
    }
    finally {
      handle.ClearIdlePause();
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

  [LoggerMessage(EventId = 1114, Level = LogLevel.Warning, Message = "Sequence {SequenceId} exceeded the {TimeoutSeconds}s per-sequence watchdog and was cancelled; treated as a non-fatal failure so the queue continues")]
  public static partial void SequenceWatchdogTimedOut(ILogger logger, string SequenceId, int TimeoutSeconds);

  [LoggerMessage(EventId = 1115, Level = LogLevel.Warning, Message = "Queue {QueueId} idle-pause could not background the game (HOME); continuing without pausing the game")]
  public static partial void IdlePauseBackgroundFailed(ILogger logger, string QueueId, Exception ex);

  [LoggerMessage(EventId = 1116, Level = LogLevel.Warning, Message = "Queue {QueueId} idle-pause could not foreground the game on resume; the due sequence will still run")]
  public static partial void IdlePauseForegroundFailed(ILogger logger, string QueueId, Exception ex);

  [LoggerMessage(EventId = 1117, Level = LogLevel.Information, Message = "Queue {QueueId} pre-session emulator ensure for instance {Instance}: {ReasonCode}")]
  public static partial void PreSessionEmulatorEnsured(ILogger logger, string QueueId, string Instance, string ReasonCode);

  [LoggerMessage(EventId = 1118, Level = LogLevel.Warning, Message = "Queue {QueueId} pre-session emulator ensure for instance {Instance} failed ({ReasonCode}); the run will not create a session")]
  public static partial void PreSessionEmulatorFailed(ILogger logger, string QueueId, string Instance, string ReasonCode);

  [LoggerMessage(EventId = 1119, Level = LogLevel.Warning, Message = "Queue {QueueId} found the game out of the foreground before sequence {SequenceId} and brought it back")]
  public static partial void ForegroundGuardRecovered(ILogger logger, string QueueId, string SequenceId);

  [LoggerMessage(EventId = 1120, Level = LogLevel.Warning, Message = "Queue {QueueId} could not bring the game back to the foreground before sequence {SequenceId} ({ReasonCode}); the firing will run anyway and the next one retries")]
  public static partial void ForegroundGuardFailed(ILogger logger, string QueueId, string SequenceId, string ReasonCode);

  [LoggerMessage(EventId = 1121, Level = LogLevel.Warning, Message = "Queue {QueueId} foreground guard faulted; treated as non-fatal and the firing proceeds")]
  public static partial void ForegroundGuardFaulted(ILogger logger, string QueueId, Exception ex);

  [LoggerMessage(EventId = 1122, Level = LogLevel.Warning, Message = "Could not read the watchdog bound for sequence {SequenceId}; falling back to the queue default")]
  public static partial void WatchdogTimeoutLookupFailed(ILogger logger, string SequenceId, Exception ex);

  [LoggerMessage(EventId = 1123, Level = LogLevel.Warning, Message = "Daily sequence {SequenceId} failed; retry {Attempt} of {MaxAttempts} armed for {DelayMinutes} minutes from now")]
  public static partial void DailyRetryArmed(ILogger logger, string SequenceId, int Attempt, int MaxAttempts, int DelayMinutes);

  [LoggerMessage(EventId = 1124, Level = LogLevel.Information, Message = "Retrying daily sequence {SequenceId} (attempt {Attempt})")]
  public static partial void DailyRetryFiring(ILogger logger, string SequenceId, int Attempt);

  [LoggerMessage(EventId = 1125, Level = LogLevel.Warning, Message = "Daily sequence {SequenceId} still failing after {MaxAttempts} retries; giving up until its next daily slot")]
  public static partial void DailyRetryExhausted(ILogger logger, string SequenceId, int MaxAttempts);

  [LoggerMessage(EventId = 1126, Level = LogLevel.Information, Message = "Queue {QueueId} execution log rotated after 24h: segment {PreviousRootId} closed, run continues in {ContinuationRootId}")]
  public static partial void ExecutionLogRotated(ILogger logger, string QueueId, string PreviousRootId, string ContinuationRootId);

  [LoggerMessage(EventId = 1127, Level = LogLevel.Warning, Message = "Queue {QueueId} could not rotate its execution log; the run continues in the current segment and retries at the next firing")]
  public static partial void ExecutionLogRotationFailed(ILogger logger, string QueueId, Exception ex);
}
