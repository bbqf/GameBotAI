using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Images;
using GameBot.Domain.Logging;
using GameBot.Domain.Services;
using GameBot.Emulator.Session;
using GameBot.Service.Services.Conditions;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging;
using EmulatorInputAction = GameBot.Emulator.Session.InputAction;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>
/// Reusable sequence-execution orchestration extracted from the <c>sequences/{id}/execute</c>
/// endpoint so the queue execution engine can run sequences with identical logging/wiring.
/// </summary>
internal sealed class SequenceExecutionService : ISequenceExecutionService {
  private readonly SequenceRunner _runner;
  private readonly TriggerEvaluationService _evalSvc;
  private readonly IImageVisibleConditionAdapter _imageVisibleConditionAdapter;
  private readonly IImageRepository _imageRepository;
  private readonly IExecutionLogService _executionLogService;
  private readonly ICommandRepository _commandRepository;
  private readonly ISequenceRepository _sequenceRepository;
  private readonly ICommandExecutor _commandExecutor;
  private readonly ISelfRescheduleCoordinator _selfRescheduleCoordinator;
  private readonly ISessionManager _sessionManager;
  private readonly IEnsureGameRunningActionHandler _ensureGameRunning;
  private readonly IEnsureEmulatorRunningActionHandler _ensureEmulatorRunning;
  private readonly ISessionService _sessionService;
  private readonly IOcrOffsetResolver _ocrOffsetResolver;
  // Feature 079: pushed for the duration of a sequence run so everything the sequence starts —
  // nested sequences, commands, loops, trigger-based image/text conditions — observes this run's own
  // device instead of "the first running session". Null only in tests that omit it.
  private readonly GameBot.Domain.Sessions.IDeviceContextAccessor? _deviceContext;
  // Only used to report a failure to close an abandoned log entry. Null in tests that omit it.
  private readonly ILogger<SequenceExecutionService>? _logger;

  public SequenceExecutionService(
    SequenceRunner runner,
    TriggerEvaluationService evalSvc,
    IImageVisibleConditionAdapter imageVisibleConditionAdapter,
    IImageRepository imageRepository,
    IExecutionLogService executionLogService,
    ICommandRepository commandRepository,
    ISequenceRepository sequenceRepository,
    ICommandExecutor commandExecutor,
    ISelfRescheduleCoordinator selfRescheduleCoordinator,
    ISessionManager sessionManager,
    IEnsureGameRunningActionHandler ensureGameRunning,
    IEnsureEmulatorRunningActionHandler ensureEmulatorRunning,
    ISessionService sessionService,
    IOcrOffsetResolver ocrOffsetResolver,
    GameBot.Domain.Sessions.IDeviceContextAccessor? deviceContext = null,
    ILogger<SequenceExecutionService>? logger = null) {
    _runner = runner;
    _evalSvc = evalSvc;
    _imageVisibleConditionAdapter = imageVisibleConditionAdapter;
    _imageRepository = imageRepository;
    _executionLogService = executionLogService;
    _commandRepository = commandRepository;
    _sequenceRepository = sequenceRepository;
    _commandExecutor = commandExecutor;
    _selfRescheduleCoordinator = selfRescheduleCoordinator;
    _sessionManager = sessionManager;
    _ensureGameRunning = ensureGameRunning;
    _ensureEmulatorRunning = ensureEmulatorRunning;
    _sessionService = sessionService;
    _ocrOffsetResolver = ocrOffsetResolver;
    _deviceContext = deviceContext;
    _logger = logger;
  }

  public Task<SequenceExecutionResult> ExecuteAsync(
      string sequenceId,
      string? sessionId,
      ExecutionLogContext? parentContext,
      CancellationToken ct = default)
    => ExecuteAsync(sequenceId, sessionId, parentContext, GameBot.Domain.Parameters.ParameterScope.Empty, ct);

  /// <summary>
  /// Executes a sequence with a parameter scope (feature 078).
  /// </summary>
  /// <param name="sequenceId">Sequence to run.</param>
  /// <param name="sessionId">Session to run against.</param>
  /// <param name="parentContext">Execution-log context linking this firing to its parent.</param>
  /// <param name="scope">
  /// Scope supplied by the caller — for a queue run, the queue built-ins layered with the firing
  /// entry's parameter values. <see cref="GameBot.Domain.Parameters.ParameterScope.Empty"/> reproduces
  /// pre-feature behaviour.
  /// </param>
  /// <param name="ct">Cancellation token.</param>
  public async Task<SequenceExecutionResult> ExecuteAsync(
      string sequenceId,
      string? sessionId,
      ExecutionLogContext? parentContext,
      GameBot.Domain.Parameters.ParameterScope scope,
      CancellationToken ct = default) {
    // The in-progress entry is opened before the first step and closed after the last one. Anything
    // that unwinds in between — most often the queue's per-sequence watchdog cancelling a firing that
    // overran, but any fault does it — used to skip that close, leaving an entry that reads "running"
    // for the rest of time. Those orphans are indistinguishable from a sequence still going, so a
    // queue could look busy while nothing was happening. Close the entry on the way out instead.
    var opened = new OpenSequenceEntry();
    try {
      return await ExecuteCoreAsync(sequenceId, sessionId, parentContext, scope, opened, ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (opened.ExecutionId is not null) {
      await FinalizeAbandonedAsync(opened, ex).ConfigureAwait(false);
      throw;
    }
  }

  /// <summary>Carries the in-progress log entry out of the core run so an abort can still close it.</summary>
  private sealed class OpenSequenceEntry {
    public string? ExecutionId { get; set; }
    public string? SequenceId { get; set; }
    public string? SequenceName { get; set; }
  }

  /// <summary>
  /// Closes a sequence entry whose run unwound before it could finalize itself, recording why. The log
  /// has exactly three statuses — success, running, failure — so an aborted run is a failure, and the
  /// distinction between "cancelled" and "faulted" lives in the summary. Uses
  /// <see cref="CancellationToken.None"/> deliberately: the common cause IS a cancelled token, and the
  /// write must still land. Best-effort — a logging failure here must not replace the original
  /// exception on its way up.
  /// </summary>
  private async Task FinalizeAbandonedAsync(OpenSequenceEntry opened, Exception ex) {
    var cancelled = ex is OperationCanceledException;
    var name = opened.SequenceName ?? opened.SequenceId ?? "sequence";
    var summary = cancelled
      ? $"Sequence '{name}' was cancelled before it finished (it exceeded its time bound, or the run was stopped)."
      : $"Sequence '{name}' ended early: {ex.GetType().Name}: {ex.Message}";
    try {
      await _executionLogService.LogSequenceFinalizeAsync(
        opened.ExecutionId!,
        opened.SequenceId ?? string.Empty,
        name,
        "failure",
        summary,
        new ExecutionLogContext {
          Depth = 0,
          SequenceId = opened.SequenceId,
          SequenceLabel = name
        },
        details: new[] {
          new ExecutionDetailItem("sequence", summary, null, "normal")
        },
        CancellationToken.None).ConfigureAwait(false);
    }
    catch (Exception logEx) {
      if (_logger is not null) SequenceExecutionLog.AbandonedFinalizeFailed(_logger, opened.ExecutionId!, logEx);
    }
  }

  private async Task<SequenceExecutionResult> ExecuteCoreAsync(
      string sequenceId,
      string? sessionId,
      ExecutionLogContext? parentContext,
      GameBot.Domain.Parameters.ParameterScope scope,
      OpenSequenceEntry opened,
      CancellationToken ct) {
    // Feature 079: bind this whole execution flow to the caller's device, so every screen observation
    // made anywhere beneath it resolves against that device. No session (an unbound, ad-hoc run) means
    // no context, and consumers fall back to the single-running-session rule.
    using var deviceScope = string.IsNullOrWhiteSpace(sessionId) || _deviceContext is null
      ? null
      : _deviceContext.Push(GameBot.Domain.Sessions.DeviceContext.For(
          sessionId!, _sessionManager.GetSession(sessionId!)?.DeviceSerial));

    // Create the in-progress root entry up front so invoked commands can be linked to it
    // (and the sequence shows as a single top-level entry while it runs). When a parent
    // context is supplied (e.g. a queue run), the sequence is nested under it instead.
    var startSequence = await _sequenceRepository.GetAsync(sequenceId).ConfigureAwait(false);
    var startSequenceName = startSequence?.Name ?? sequenceId;
    var rootExecutionId = parentContext is null
      ? await _executionLogService.LogSequenceStartAsync(sequenceId, startSequenceName, ct).ConfigureAwait(false)
      : await _executionLogService.LogSequenceStartAsync(sequenceId, startSequenceName, parentContext, ct).ConfigureAwait(false);
    // Publish the open entry so an abort further down can still close it.
    opened.ExecutionId = rootExecutionId;
    opened.SequenceId = sequenceId;
    opened.SequenceName = startSequenceName;
    var childRootExecutionId = string.IsNullOrWhiteSpace(parentContext?.RootExecutionId) ? rootExecutionId : parentContext!.RootExecutionId!;
    var childDepth = (parentContext?.Depth ?? 0) + 1;
    var childInvocationIndex = 0;
    // Origin of this firing: the queue run that launched it, propagated through nesting (FR-018).
    // Non-empty ⇒ a self-reschedule action can schedule into that run; empty ⇒ no-op success (FR-011).
    var originatingQueueId = parentContext?.OriginatingQueueId;
    // When this firing was itself produced by a self-reschedule, carry the originating action id so
    // the extra firing is attributable in the execution log (feature 065, FR-014).
    var selfRescheduleOriginActionId = parentContext?.SelfRescheduleOriginActionId;

    // Invokes one command step and reports whether its input actually reached the device, so the
    // runner can record an honest outcome instead of assuming "executed" whenever nothing threw.
    async Task<GameBot.Domain.Services.CommandDispatchOutcome> DispatchCommandAsync(string commandId, GameBot.Domain.Parameters.ParameterScope stepScope) {
      {
        try {
          var childContext = new ExecutionLogContext {
            ParentExecutionId = rootExecutionId,
            RootExecutionId = childRootExecutionId,
            Depth = childDepth,
            SequenceIndex = ++childInvocationIndex,
            SequenceId = sequenceId,
            SequenceLabel = startSequenceName,
            OriginatingQueueId = originatingQueueId
          };
          var detailed = await _commandExecutor.ForceExecuteDetailedAsync(sessionId, commandId, childContext, stepScope, ct).ConfigureAwait(false);
          return ClassifyDispatch(detailed, ct);
        }
        catch (KeyNotFoundException ex) when (ex.Message == "cached_session_not_found") {
          throw new InvalidOperationException($"No cached session found for command '{commandId}'. Start a session first.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "missing_session_context") {
          throw new InvalidOperationException($"No session available for command '{commandId}'. Start a session or pass a sessionId.");
        }
        catch (KeyNotFoundException ex) {
          // Primitive action steps (tap/swipe/key/connect-to-game/ensure-game-running) never
          // reach this path — they are dispatched via the action dispatcher below. What lands
          // here is a dangling reference (missing command or session); it must fail the step
          // loudly instead of reporting a fake success.
          var reason = ex.Message == "Command not found"
            ? $"Command '{commandId}' was not found; the sequence step references a missing command."
            : $"Command '{commandId}' could not be executed: {ex.Message}.";
          throw new InvalidOperationException(reason);
        }
      }
    }

    var res = await _runner.ExecuteAsync(
      sequenceId,
      // Never reached in production — the runner prefers the dispatcher below — but kept a real
      // call rather than a throwing stub so the legacy contract stays honest.
      async (commandId, stepScope) => await DispatchCommandAsync(commandId, stepScope).ConfigureAwait(false),
      commandDispatcher: DispatchCommandAsync,
      gateEvaluator: (step, token) => {
        // Temporary evaluator for integration tests:
        // TargetId "always" => gate passes; "never" => gate fails
        if (step.Gate == null) return Task.FromResult(true);
        var tid = step.Gate.TargetId ?? string.Empty;
        if (string.Equals(tid, "always", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
        if (string.Equals(tid, "never", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(false);
        return Task.FromResult(true);
      },
      conditionEvaluator: (cond, token) => {
        if (string.Equals(cond.Source, "image", StringComparison.OrdinalIgnoreCase)) {
          return EvaluateImageConditionAsync(cond, _imageRepository, _imageVisibleConditionAdapter, token);
        }
        if (string.Equals(cond.Source, "text", StringComparison.OrdinalIgnoreCase)) {
          var region = cond.Region is null ? new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
                                           : new GameBot.Domain.Triggers.Region { X = cond.Region.X, Y = cond.Region.Y, Width = cond.Region.Width, Height = cond.Region.Height };
          var mode = string.Equals(cond.Mode, "Absent", StringComparison.OrdinalIgnoreCase) ? "not-found" : "found";
          var trig = new GameBot.Domain.Triggers.Trigger {
            Id = "inline-text",
            Type = GameBot.Domain.Triggers.TriggerType.TextMatch,
            Enabled = true,
            Params = new GameBot.Domain.Triggers.TextMatchParams {
              Target = cond.TargetId,
              Region = region,
              ConfidenceThreshold = cond.ConfidenceThreshold ?? 0.80,
              Mode = mode,
              Language = cond.Language
            }
          };
          var r = _evalSvc.Evaluate(trig, DateTimeOffset.UtcNow);
          return Task.FromResult(r.Status == GameBot.Domain.Triggers.TriggerStatus.Satisfied);
        }
        return Task.FromResult(false);
      },
      ct: ct,
      scope: scope,
      actionDispatcher: (action, token) => DispatchActionAsync(action, sequenceId, originatingQueueId, sessionId, token)
    ).ConfigureAwait(false);

    var sequence = await _sequenceRepository.GetAsync(sequenceId).ConfigureAwait(false);
    var sequenceName = sequence?.Name ?? sequenceId;
    var status = string.Equals(res.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? "success" : "failure";
    var flattenedSequenceSteps = FlattenSequenceSteps(sequence?.Steps ?? Array.Empty<SequenceStep>()).ToArray();
    var sequenceStepsByCommandId = flattenedSequenceSteps
      .Where(step => !string.IsNullOrWhiteSpace(step.CommandId))
      .GroupBy(step => step.CommandId, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    // feature 065: reschedule-self steps have no commandId; index them by stepId for log enrichment.
    var sequenceStepsByStepId = flattenedSequenceSteps
      .Where(step => !string.IsNullOrWhiteSpace(step.StepId))
      .GroupBy(step => step.StepId, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var flowStepsByCommandRef = (sequence?.FlowSteps ?? Array.Empty<FlowStep>())
      .GroupBy(step => string.IsNullOrWhiteSpace(step.PayloadRef) ? step.StepId : step.PayloadRef!, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var commandNamesById = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var commandId in res.Steps
      .Where(step => step.LoopIterations is null && !string.IsNullOrWhiteSpace(step.CommandId))
      .Select(step => step.CommandId)
      .Distinct(StringComparer.Ordinal)) {
      var command = await _commandRepository.GetAsync(commandId, ct).ConfigureAwait(false);
      if (command is not null && !string.IsNullOrWhiteSpace(command.Name)) {
        commandNamesById[commandId] = command.Name;
      }
    }

    var commandSteps = res.Steps.Where(s => s.LoopIterations is null).ToList();
    var detailItems = new List<ExecutionDetailItem> {
      new(
        "sequence",
        $"Executed commands: {string.Join(",", commandSteps.Select(s => s.CommandId).Take(10))}",
        new Dictionary<string, object?> {
          ["executedCount"] = commandSteps.Count,
          // feature 065: mark firings produced by a self-reschedule so they are attributable (FR-014).
          ["selfRescheduleOrigin"] = string.IsNullOrWhiteSpace(selfRescheduleOriginActionId) ? null : true,
          ["selfRescheduleOriginActionId"] = selfRescheduleOriginActionId
        },
        "normal")
    };
    if (!string.IsNullOrWhiteSpace(selfRescheduleOriginActionId)) {
      detailItems.Add(new ExecutionDetailItem(
        "note",
        $"Scheduled by self-reschedule (origin action {selfRescheduleOriginActionId}).",
        new Dictionary<string, object?> {
          ["selfRescheduleOrigin"] = true,
          ["selfRescheduleOriginActionId"] = selfRescheduleOriginActionId
        },
        "normal"));
    }

    var stepOrder = 1;
    foreach (var step in res.Steps) {
      flowStepsByCommandRef.TryGetValue(step.CommandId, out var flowStep);
      sequenceStepsByCommandId.TryGetValue(step.CommandId, out var sequenceStep);
      var stepId = flowStep?.StepId ?? sequenceStep?.StepId ?? step.CommandId;
      var stepLabel = flowStep?.Label ?? sequenceStep?.Label ?? sequenceStep?.StepId ?? step.CommandId;

      if (step.LoopIterations is not null) {
        var iterCount = step.LoopIterations.Count;
        detailItems.Add(new ExecutionDetailItem(
          "step",
          $"Loop '{stepLabel}' {step.Status.ToLowerInvariant()} after {iterCount} iteration{(iterCount == 1 ? "" : "s")}.",
          new Dictionary<string, object?> {
            ["stepOrder"] = stepOrder++,
            ["stepType"] = "loop",
            ["status"] = step.Status,
            ["actionOutcome"] = step.Status.ToLowerInvariant(),
            ["appliedDelayMs"] = step.InterStepDelayMs ?? step.AppliedDelayMs,
            ["stepDelayMs"] = step.AppliedDelayMs,
            ["interStepDelayMs"] = step.InterStepDelayMs,
            ["iterations"] = iterCount,
            ["message"] = step.Message,
            ["sequenceId"] = sequenceId,
            ["sequenceLabel"] = sequenceName,
            ["stepId"] = stepId,
            ["stepLabel"] = stepLabel
          },
          "normal"));
        continue;
      }

      var actionOutcome = string.IsNullOrWhiteSpace(step.ActionOutcome)
        ? (string.Equals(step.Status, "Skipped", StringComparison.OrdinalIgnoreCase) ? "skipped" : "executed")
        : step.ActionOutcome;

      // feature 065: render a self-reschedule decision as its own log entry (option, resolved timing,
      // current-run-only, outcome + reason). Outcomes "scheduled"/"noop" are unique to this action.
      sequenceStepsByStepId.TryGetValue(step.CommandId, out var stepById);
      var isRescheduleStep =
        string.Equals(stepById?.Action?.Type, ActionTypes.RescheduleSelf, StringComparison.OrdinalIgnoreCase)
        || string.Equals(actionOutcome, "scheduled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(actionOutcome, "noop", StringComparison.OrdinalIgnoreCase);
      if (isRescheduleStep) {
        string? option = null;
        if (stepById?.Action is not null
            && SelfReschedulePayload.TryRead(stepById.Action, out var reschedulePayload, out _)
            && reschedulePayload is not null) {
          option = reschedulePayload.Option.ToString();
        }
        detailItems.Add(new ExecutionDetailItem(
          "step",
          !string.IsNullOrWhiteSpace(step.Message)
            ? $"Self-reschedule '{stepLabel}' {actionOutcome}: {step.Message}"
            : $"Self-reschedule '{stepLabel}' {actionOutcome}.",
          new Dictionary<string, object?> {
            ["stepOrder"] = stepOrder++,
            ["stepType"] = "reschedule-self",
            ["status"] = step.Status,
            ["actionOutcome"] = actionOutcome,
            ["reasonCode"] = actionOutcome,
            ["option"] = option,
            ["resolvedTiming"] = step.Message,
            ["currentRunOnly"] = true,
            ["message"] = step.Message,
            ["sequenceId"] = sequenceId,
            ["sequenceLabel"] = sequenceName,
            ["stepId"] = stepId,
            ["stepLabel"] = stepLabel
          },
          "normal"));
        continue;
      }

      // feature 067: render an if-step branch decision as its own log entry. The runner records
      // the decision (conditionType/result, actionOutcome then|else|none) before the branch steps.
      var isIfStep = stepById?.StepType == SequenceStepType.If
        || string.Equals(actionOutcome, "then", StringComparison.OrdinalIgnoreCase)
        || string.Equals(actionOutcome, "else", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(actionOutcome, "none", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(step.ConditionResult));
      if (isIfStep) {
        detailItems.Add(new ExecutionDetailItem(
          "step",
          !string.IsNullOrWhiteSpace(step.Message)
            ? step.Message
            : $"If '{stepLabel}' {actionOutcome}.",
          new Dictionary<string, object?> {
            ["stepOrder"] = stepOrder++,
            ["stepType"] = "if",
            ["status"] = step.Status,
            ["actionOutcome"] = actionOutcome,
            ["reasonCode"] = actionOutcome,
            ["conditionType"] = step.ConditionType,
            ["conditionResult"] = step.ConditionResult,
            ["message"] = step.Message,
            ["sequenceId"] = sequenceId,
            ["sequenceLabel"] = sequenceName,
            ["stepId"] = stepId,
            ["stepLabel"] = stepLabel
          },
          "normal"));
        continue;
      }

      var waitDetails = step.WaitForImageDetails;
      var waitConfig = sequenceStep?.WaitForImage;
      var isWaitForImageStep = waitDetails is not null
        || waitConfig is not null
        || string.Equals(sequenceStep?.Action?.Type, "WaitForImage", StringComparison.OrdinalIgnoreCase);
      commandNamesById.TryGetValue(step.CommandId, out var commandName);
      var stepType = isWaitForImageStep ? "waitForImage" : "command";
      var imageLoadStatus = waitDetails?.ImageLoadStatus
        ?? (waitConfig?.DetectionTarget is null
          ? null
          : string.Equals(actionOutcome, "image_unavailable", StringComparison.OrdinalIgnoreCase)
            ? "unavailable"
            : "loaded");
      var stepDisplayMessage = isWaitForImageStep || string.IsNullOrWhiteSpace(commandName)
        ? (!string.IsNullOrWhiteSpace(step.Message)
          ? $"Step '{stepLabel}' {actionOutcome}: {step.Message}"
          : $"Step '{stepLabel}' {actionOutcome}.")
        : (!string.IsNullOrWhiteSpace(step.Message)
          ? $"Step '{stepLabel}' ran command '{commandName}' with outcome '{actionOutcome}': {step.Message}"
          : $"Step '{stepLabel}' ran command '{commandName}' with outcome '{actionOutcome}'.");
      detailItems.Add(new ExecutionDetailItem(
        "step",
        stepDisplayMessage,
        new Dictionary<string, object?> {
          ["stepOrder"] = stepOrder++,
          ["stepType"] = stepType,
          ["status"] = step.Status,
          ["actionOutcome"] = actionOutcome,
          ["reasonCode"] = actionOutcome,
          ["appliedDelayMs"] = step.InterStepDelayMs ?? step.AppliedDelayMs,
          ["stepDelayMs"] = step.AppliedDelayMs,
          ["interStepDelayMs"] = step.InterStepDelayMs,
          ["conditionType"] = step.ConditionType,
          ["conditionResult"] = step.ConditionResult,
          ["message"] = step.Message,
          ["timeoutMs"] = waitDetails?.TimeoutMs ?? waitConfig?.TimeoutMs,
          ["effectiveTimeoutMs"] = waitDetails?.EffectiveTimeoutMs ?? waitConfig?.TimeoutMs,
          ["referenceImageId"] = waitDetails?.ReferenceImageId ?? waitConfig?.DetectionTarget?.ReferenceImageId,
          ["confidence"] = waitDetails?.Confidence ?? waitConfig?.DetectionTarget?.Confidence,
          ["exitCondition"] = isWaitForImageStep ? (waitDetails?.ExitCondition ?? actionOutcome) : null,
          ["imageLoadStatus"] = imageLoadStatus,
          ["sequenceId"] = sequenceId,
          ["sequenceLabel"] = sequenceName,
          ["stepId"] = stepId,
          ["stepLabel"] = stepLabel,
          ["commandName"] = isWaitForImageStep ? null : commandName,
          ["commandId"] = isWaitForImageStep ? null : step.CommandId
        },
        "normal"));
    }

    foreach (var trace in res.ConditionTraces) {
      detailItems.Add(new ExecutionDetailItem(
        "step",
        $"Condition step '{trace.StepLabel ?? trace.StepId}' evaluated to {trace.Trace.FinalResult}.",
        new Dictionary<string, object?> {
          ["stepOrder"] = stepOrder++,
          ["stepType"] = "condition",
          ["status"] = "executed",
          ["conditionResult"] = trace.Trace.FinalResult,
          ["actionOutcome"] = trace.Trace.FinalResult ? "executed" : "skipped",
          ["sequenceId"] = sequenceId,
          ["sequenceLabel"] = sequenceName,
          ["stepId"] = trace.StepId,
          ["stepLabel"] = trace.StepLabel ?? trace.StepId,
          ["conditionTrace"] = trace.Trace
        },
        "normal"));
    }

    await _executionLogService.LogSequenceFinalizeAsync(
      rootExecutionId,
      sequenceId,
      sequenceName,
      status,
      $"Sequence '{sequenceName}' {status} with {commandSteps.Count} step{(commandSteps.Count == 1 ? "" : "s")} executed.",
      new ExecutionLogContext {
        Depth = 0,
        SequenceId = sequenceId,
        SequenceLabel = sequenceName
      },
      details: detailItems,
      ct).ConfigureAwait(false);
    return res;
  }

  /// <summary>
  /// Dispatches a <c>reschedule-self</c> action (feature 065). When the sequence was not started
  /// from a queue (<paramref name="originatingQueueId"/> empty), it is a success no-op (FR-011).
  /// Otherwise it asks the coordinator to inject one ephemeral firing into the originating run and
  /// records the decision (option + resolved timing) for the execution log (FR-013).
  /// </summary>
  private ActionDispatchResult DispatchSelfReschedule(
      SequenceActionPayload action,
      string sequenceId,
      string? originatingQueueId,
      string? sessionId) {
    if (string.IsNullOrWhiteSpace(originatingQueueId)) {
      return new ActionDispatchResult("noop", "no originating queue, no reschedule performed");
    }

    if (!SelfReschedulePayload.TryRead(action, out var payload, out var parseError) || payload is null) {
      return new ActionDispatchResult("noop", $"self-reschedule not performed: {parseError}");
    }

    // feature 068: when an ocrOffset spec is present (Timer), derive the relative offset at runtime
    // by OCR-reading the on-screen countdown, falling back to the static offset on any failure.
    var timerTimeOfDay = payload.TimerTimeOfDay;
    var timerRelativeOffset = payload.TimerRelativeOffset;
    OcrOffsetResolution? ocrResolution = null;
    if (payload.HasOcrOffset && payload.Option == SelfRescheduleOption.Timer) {
      ocrResolution = _ocrOffsetResolver.Resolve(sessionId, payload.OcrOffset!);
      timerTimeOfDay = null;
      timerRelativeOffset = ocrResolution.EffectiveOffset;
    }

    var schedule = _selfRescheduleCoordinator.ScheduleSelf(
      originatingQueueId!,
      sequenceId,
      payload.Option,
      timerTimeOfDay,
      timerRelativeOffset);

    if (schedule.Outcome == SelfRescheduleOutcome.NotRunning) {
      return new ActionDispatchResult("noop", "originating queue run no longer active; no reschedule performed");
    }

    var message = ocrResolution is null
      ? $"rescheduled this sequence (option {schedule.Option}, {schedule.ResolvedTiming}); applies to the current run only"
      : $"rescheduled this sequence (option {schedule.Option}, {schedule.ResolvedTiming}); {DescribeOcrOffset(ocrResolution)}; applies to the current run only";

    return new ActionDispatchResult("scheduled", message);
  }

  // feature 068: renders the offset-source detail for the execution log (FR-007).
  // internal for unit testing the log-message content (SC-004).
  internal static string DescribeOcrOffset(OcrOffsetResolution resolution) {
    if (string.Equals(resolution.Source, OcrOffsetSource.Ocr, StringComparison.Ordinal)) {
      return $"offset source ocr (read '{resolution.RecognizedText}' -> {FormatOffset(resolution.EffectiveOffset)})";
    }

    var readSuffix = string.IsNullOrEmpty(resolution.RecognizedText)
      ? string.Empty
      : $", read '{resolution.RecognizedText}'";
    return $"offset source fallback (reason {resolution.Reason}{readSuffix}, using {FormatOffset(resolution.EffectiveOffset)})";
  }

  private static string FormatOffset(TimeSpan offset) =>
    offset.ToString("c", CultureInfo.InvariantCulture);

  /// <summary>
  /// Routes a non-command sequence action to its handler: <c>reschedule-self</c> to the
  /// coordinator (feature 065), <c>connect-to-game</c> to the session service,
  /// <c>ensure-game-running</c> to its action handler, and primitive inputs (tap/swipe/key)
  /// to the session input pipeline.
  /// </summary>
  private Task<ActionDispatchResult> DispatchActionAsync(
      SequenceActionPayload action,
      string sequenceId,
      string? originatingQueueId,
      string? sessionId,
      CancellationToken ct) {
    if (string.Equals(action.Type, ActionTypes.RescheduleSelf, StringComparison.OrdinalIgnoreCase)) {
      return Task.FromResult(DispatchSelfReschedule(action, sequenceId, originatingQueueId, sessionId));
    }

    if (string.Equals(action.Type, ActionTypes.ConnectToGame, StringComparison.OrdinalIgnoreCase)) {
      return DispatchConnectToGameAsync(action, ct);
    }

    if (string.Equals(action.Type, ActionTypes.EnsureGameRunning, StringComparison.OrdinalIgnoreCase)) {
      return DispatchEnsureGameRunningAsync(sessionId, ct);
    }

    if (string.Equals(action.Type, ActionTypes.GoToHomeScreen, StringComparison.OrdinalIgnoreCase)) {
      return DispatchGoToHomeScreenAsync(sessionId, ct);
    }

    if (string.Equals(action.Type, ActionTypes.EnsureEmulatorRunning, StringComparison.OrdinalIgnoreCase)) {
      return DispatchEnsureEmulatorRunningAsync(action, ct);
    }

    return DispatchPrimitiveInputAsync(action, sessionId, ct);
  }

  /// <summary>
  /// Handles a <c>connect-to-game</c> sequence step by starting (or restarting) a session for
  /// the game/device named in the step parameters — the same session operation as the
  /// <c>/api/sessions/start</c> endpoint — and then bringing the game to the foreground.
  /// Starting a session only attaches ADB/screen capture; it does not launch the app, so a
  /// connect against a device where the game process is not running would otherwise leave
  /// nothing on screen. After the session is up we reuse the <c>ensure-game-running</c> handler
  /// for its foreground check + best-effort launch. The launch is best-effort: whether the game
  /// was already running or had to be launched (and on non-Windows, where ADB is unavailable),
  /// the connect step still succeeds — the session is attached either way. Returns a
  /// <c>failed</c> outcome — which fails the step and the sequence — only when parameters are
  /// missing or the session itself cannot be started.
  /// </summary>
  private async Task<ActionDispatchResult> DispatchConnectToGameAsync(
      SequenceActionPayload action,
      CancellationToken ct) {
    if (!TryGetString(action.Parameters, "gameId", out var gameId)
        || !TryGetString(action.Parameters, "adbSerial", out var adbSerial)) {
      return new ActionDispatchResult(
        "failed",
        "connect-to-game step requires 'gameId' and 'adbSerial' parameters");
    }

    // Feature 071: when the connect action carries an LDPlayer instance identifier, ensure that
    // emulator instance is running/responsive first. A genuine emulator failure (recovery timeout /
    // instance not found) fails the step before any session is started; success or a neutral
    // unsupported outcome proceeds. Absent an instance identifier, no emulator work happens.
    var emu = await PreheatEmulatorAsync(action, ct).ConfigureAwait(false);
    var preheatFailure = ConnectEmulatorPreheat.FailFastReason(emu);
    if (preheatFailure is not null) {
      return new ActionDispatchResult("failed", preheatFailure);
    }

    string startedSessionId;
    try {
      var started = _sessionService.StartSession(gameId!, adbSerial!, ct);
      startedSessionId = started.SessionId;
    }
    catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or ArgumentException) {
      return new ActionDispatchResult(
        "failed",
        $"connect-to-game failed for game '{gameId}' on device '{adbSerial}': {ex.Message}");
    }

    // Launch the game after connecting so a connect on a device where the game is not running
    // actually brings it up. Best-effort: a launch (game_not_running) or unsupported platform
    // does not fail the connect step.
    var launch = await _ensureGameRunning.ExecuteAsync(startedSessionId, ct).ConfigureAwait(false);
    var emulatorNote = ConnectEmulatorPreheat.MessageClause(emu);
    return new ActionDispatchResult(
      "executed",
      $"connected to game '{gameId}' on device '{adbSerial}' (session {startedSessionId}); {emulatorNote}game launch: {launch.ReasonCode}");
  }

  /// <summary>
  /// Runs the feature-070 emulator health-and-recover handler when the connect action carries an
  /// LDPlayer instance identifier (name or index); returns <c>null</c> when none is supplied, so the
  /// connect proceeds unchanged.
  /// </summary>
  private async Task<EnsureEmulatorRunningActionResult?> PreheatEmulatorAsync(
      SequenceActionPayload action,
      CancellationToken ct) {
    if (!EnsureEmulatorRunningArgs.TryFrom(action.Parameters, out var emuArgs)) return null;
    return await _ensureEmulatorRunning.ExecuteAsync(emuArgs, ct).ConfigureAwait(false);
  }

  /// <summary>
  /// Handles an <c>ensure-game-running</c> sequence step through the same handler the command
  /// executor uses for its command-step equivalent. Success means the linked game is the
  /// foreground app; anything else (game not running — a launch is attempted, missing
  /// context/config, unsupported platform) is a <c>failed</c> outcome that fails the step
  /// and stops the sequence.
  /// </summary>
  private async Task<ActionDispatchResult> DispatchEnsureGameRunningAsync(
      string? sessionId,
      CancellationToken ct) {
    if (!TryResolveSessionId(sessionId, "ensure-game-running", out var resolvedSessionId, out var resolveError)) {
      return new ActionDispatchResult("failed", resolveError);
    }

    var result = await _ensureGameRunning.ExecuteAsync(resolvedSessionId!, ct).ConfigureAwait(false);
    return result.IsSuccess
      ? new ActionDispatchResult("executed", "game is running in the foreground (game_running)")
      : new ActionDispatchResult("failed", $"ensure-game-running failed: {result.ReasonCode}");
  }

  /// <summary>
  /// Handles an <c>ensure-emulator-running</c> sequence step (feature 070) by verifying the target
  /// LDPlayer instance is running and responsive, starting/restarting it via the handler when it is
  /// not. Reads the instance identifier (name or index) and adbSerial from the step parameters.
  /// A healthy/started/restarted outcome — and the neutral unsupported outcomes on hosts that cannot
  /// drive the emulator — succeed the step; a recovery timeout or nonexistent instance fails it.
  /// </summary>
  private async Task<ActionDispatchResult> DispatchEnsureEmulatorRunningAsync(
      SequenceActionPayload action,
      CancellationToken ct) {
    if (!EnsureEmulatorRunningArgs.TryFrom(action.Parameters, out var args)) {
      return new ActionDispatchResult(
        "failed",
        "ensure-emulator-running step requires 'adbSerial' and an instance 'instanceName' or 'instanceIndex'");
    }

    var result = await _ensureEmulatorRunning.ExecuteAsync(args, ct).ConfigureAwait(false);
    return result.IsSuccess || result.IsUnsupported
      ? new ActionDispatchResult("executed", result.Message)
      : new ActionDispatchResult("failed", $"ensure-emulator-running failed: {result.Message}");
  }

  /// <summary>
  /// Resolves the device session a step must act on, delegating to the shared
  /// <see cref="SessionResolver"/> rule (feature 079, FR-006/FR-007).
  /// </summary>
  private bool TryResolveSessionId(string? sessionId, string stepType, out string? resolved, out string error) =>
    SessionResolver.TryResolve(_sessionManager, sessionId, stepType, out resolved, out error);

  // Android KEYCODE_HOME. Pressing it returns the device to the home/main screen without stopping
  // the foreground app, so the game keeps running in the background (feature 069).
  private const int AndroidKeyCodeHome = 3;

  /// <summary>
  /// Handles a <c>go-to-home-screen</c> sequence step by sending the Android HOME key to the session
  /// (feature 069). The game is left running in the background — HOME does not force-stop it. Reuses
  /// the session input pipeline, which retries on Windows/ADB and returns a stub success on
  /// non-Windows or non-ADB sessions (graceful degradation). Returns a <c>failed</c> outcome — which
  /// fails the step and the sequence — when no running session can be resolved or the device rejects
  /// the input.
  /// </summary>
  private async Task<ActionDispatchResult> DispatchGoToHomeScreenAsync(
      string? sessionId,
      CancellationToken ct) {
    if (!TryResolveSessionId(sessionId, "go-to-home-screen", out var resolvedSessionId, out var resolveError)) {
      return new ActionDispatchResult("failed", resolveError);
    }

    var input = new EmulatorInputAction("key", new Dictionary<string, object> { ["keyCode"] = AndroidKeyCodeHome });
    var accepted = await _sessionManager.SendInputsAsync(resolvedSessionId!, new[] { input }, ct).ConfigureAwait(false);
    return accepted > 0
      ? new ActionDispatchResult("executed", "pressed HOME; device returned to the home screen (game left running)")
      : new ActionDispatchResult("failed", $"'go-to-home-screen' input was not accepted by session '{resolvedSessionId}'");
  }

  /// <summary>
  /// Sends a primitive tap/swipe/key sequence step to the emulator session input pipeline.
  /// Returns a <c>failed</c> outcome — which fails the step and the sequence — when the payload
  /// is incomplete, no running session can be resolved, or the device rejects the input.
  /// </summary>
  private async Task<ActionDispatchResult> DispatchPrimitiveInputAsync(
      SequenceActionPayload action,
      string? sessionId,
      CancellationToken ct) {
    if (!TryBuildInputAction(action, out var input, out var error)) {
      return new ActionDispatchResult("failed", error);
    }

    if (!TryResolveSessionId(sessionId, action.Type, out var resolvedSessionId, out var resolveError)) {
      return new ActionDispatchResult("failed", resolveError);
    }

    var accepted = await _sessionManager.SendInputsAsync(resolvedSessionId!, new[] { input! }, ct).ConfigureAwait(false);
    if (accepted == 0) {
      return new ActionDispatchResult(
        "failed",
        $"'{action.Type}' input was not accepted by session '{resolvedSessionId}'");
    }

    // Describe after sending: the session manager applies tap-point jitter by mutating the
    // args in place, so the message reflects the coordinates actually sent to the device.
    return new ActionDispatchResult("executed", DescribeInput(input!));
  }

  private static bool TryBuildInputAction(SequenceActionPayload action, out EmulatorInputAction? input, out string? error) {
    input = null;
    error = null;

    if (string.Equals(action.Type, ActionTypes.Tap, StringComparison.OrdinalIgnoreCase)) {
      if (!TryGetInt(action.Parameters, "x", out var x) || !TryGetInt(action.Parameters, "y", out var y)) {
        error = "tap step requires numeric 'x' and 'y' parameters";
        return false;
      }
      input = new EmulatorInputAction("tap", new Dictionary<string, object> { ["x"] = x, ["y"] = y });
      return true;
    }

    if (string.Equals(action.Type, ActionTypes.Swipe, StringComparison.OrdinalIgnoreCase)) {
      if (!TryGetInt(action.Parameters, "x1", out var x1) || !TryGetInt(action.Parameters, "y1", out var y1)
          || !TryGetInt(action.Parameters, "x2", out var x2) || !TryGetInt(action.Parameters, "y2", out var y2)) {
        error = "swipe step requires numeric 'x1', 'y1', 'x2' and 'y2' parameters";
        return false;
      }
      var swipeArgs = new Dictionary<string, object> { ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 };
      var durationMs = TryGetInt(action.Parameters, "durationMs", out var duration) ? duration : (int?)null;
      input = new EmulatorInputAction("swipe", swipeArgs, null, durationMs);
      return true;
    }

    if (string.Equals(action.Type, ActionTypes.Key, StringComparison.OrdinalIgnoreCase)) {
      if (TryGetInt(action.Parameters, "keyCode", out var keyCode)) {
        input = new EmulatorInputAction("key", new Dictionary<string, object> { ["keyCode"] = keyCode });
        return true;
      }
      if (TryGetString(action.Parameters, "key", out var key)) {
        input = new EmulatorInputAction("key", new Dictionary<string, object> { ["key"] = key! });
        return true;
      }
      error = "key step requires a 'key' or 'keyCode' parameter";
      return false;
    }

    error = $"action type '{action.Type}' has no input dispatch";
    return false;
  }

  // Parameters arrive as JsonElement from persisted sequences, as CLR primitives from in-process
  // callers, and as strings after {{iteration}} template substitution in loop bodies.
  private static bool TryGetInt(Dictionary<string, object?> parameters, string key, out int value) {
    value = 0;
    if (!parameters.TryGetValue(key, out var raw) || raw is null) return false;
    switch (raw) {
      case JsonElement je when je.ValueKind == JsonValueKind.Number:
        return je.TryGetInt32(out value);
      case JsonElement je when je.ValueKind == JsonValueKind.String:
        return int.TryParse(je.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
      case JsonElement:
        return false;
      case string s:
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
      default:
        try {
          value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
          return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) {
          return false;
        }
    }
  }

  private static bool TryGetString(Dictionary<string, object?> parameters, string key, out string? value) {
    value = null;
    if (!parameters.TryGetValue(key, out var raw) || raw is null) return false;
    value = raw is JsonElement je
      ? (je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString())
      : raw.ToString();
    return !string.IsNullOrWhiteSpace(value);
  }

  private static string DescribeInput(EmulatorInputAction input) {
    return input.Type switch {
      "tap" => $"tap({input.Args["x"]},{input.Args["y"]}) sent to emulator",
      "swipe" => $"swipe({input.Args["x1"]},{input.Args["y1"]} -> {input.Args["x2"]},{input.Args["y2"]}) sent to emulator",
      _ => "key input sent to emulator"
    };
  }

  private static async Task<bool> EvaluateImageConditionAsync(
    GameBot.Domain.Commands.Blocks.Condition cond,
    IImageRepository imageRepository,
    IImageVisibleConditionAdapter imageVisibleConditionAdapter,
    CancellationToken token) {
    if (!string.IsNullOrWhiteSpace(cond.TargetId)
        && !await imageRepository.ExistsAsync(cond.TargetId, token).ConfigureAwait(false)) {
      throw new InvalidOperationException("image_unavailable");
    }

    return await imageVisibleConditionAdapter.EvaluateAsync(cond, token).ConfigureAwait(false);
  }

  /// <summary>
  /// Command step types that put input on the device. Only these can make a command "dispatch
  /// nothing": waiting for an image is observational, and the ensure-emulator/ensure-game steps
  /// carry their own reason codes (a deliberately optional readiness gate among them), so neither
  /// says anything about whether a tap landed.
  /// </summary>
  private static bool IsInputBearing(PrimitiveTapStepOutcome outcome) =>
    outcome.StepType is null                       // primitive tap: the only kind that leaves StepType unset
    || string.Equals(outcome.StepType, "key", StringComparison.OrdinalIgnoreCase)
    || string.Equals(outcome.StepType, "swipe", StringComparison.OrdinalIgnoreCase)
    || string.Equals(outcome.StepType, "go-to-home-screen", StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// Decides whether a finished command actually put input on the device. A command with no
  /// input-bearing steps at all (a pure wait, say) counts as dispatched: it did everything it was
  /// asked to. Cancellation is deliberately NOT reported as a miss — a stopped queue or a tripped
  /// watchdog must keep surfacing as cancellation rather than as a sequence failure.
  /// </summary>
  private static CommandDispatchOutcome ClassifyDispatch(CommandForceExecutionResult result, CancellationToken ct) {
    if (ct.IsCancellationRequested) return CommandDispatchOutcome.Executed;

    var inputSteps = result.StepOutcomes.Where(IsInputBearing).ToList();
    if (inputSteps.Count == 0) return CommandDispatchOutcome.Executed;
    if (inputSteps.Any(o => string.Equals(o.Status, "executed", StringComparison.OrdinalIgnoreCase)))
      return CommandDispatchOutcome.Executed;
    if (inputSteps.Any(o => string.Equals(o.Status, "cancelled", StringComparison.OrdinalIgnoreCase)))
      return CommandDispatchOutcome.Executed;

    var first = inputSteps[0];
    return new CommandDispatchOutcome(false, first.Reason ?? first.Status);
  }

  private static IEnumerable<SequenceStep> FlattenSequenceSteps(IEnumerable<SequenceStep> steps) {
    foreach (var step in steps) {
      yield return step;
      if (step.Body.Count == 0) {
        continue;
      }
      foreach (var child in FlattenSequenceSteps(step.Body)) {
        yield return child;
      }
    }
  }
}

internal static partial class SequenceExecutionLog {
  [LoggerMessage(EventId = 1130, Level = LogLevel.Warning, Message = "Could not close abandoned sequence execution-log entry {ExecutionId}; it will keep reading as running")]
  public static partial void AbandonedFinalizeFailed(ILogger logger, string ExecutionId, Exception ex);
}
