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
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
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
  private readonly ISessionService _sessionService;
  private readonly IOcrOffsetResolver _ocrOffsetResolver;

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
    ISessionService sessionService,
    IOcrOffsetResolver ocrOffsetResolver) {
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
    _sessionService = sessionService;
    _ocrOffsetResolver = ocrOffsetResolver;
  }

  public async Task<SequenceExecutionResult> ExecuteAsync(
      string sequenceId,
      string? sessionId,
      ExecutionLogContext? parentContext,
      CancellationToken ct = default) {
    // Create the in-progress root entry up front so invoked commands can be linked to it
    // (and the sequence shows as a single top-level entry while it runs). When a parent
    // context is supplied (e.g. a queue run), the sequence is nested under it instead.
    var startSequence = await _sequenceRepository.GetAsync(sequenceId).ConfigureAwait(false);
    var startSequenceName = startSequence?.Name ?? sequenceId;
    var rootExecutionId = parentContext is null
      ? await _executionLogService.LogSequenceStartAsync(sequenceId, startSequenceName, ct).ConfigureAwait(false)
      : await _executionLogService.LogSequenceStartAsync(sequenceId, startSequenceName, parentContext, ct).ConfigureAwait(false);
    var childRootExecutionId = string.IsNullOrWhiteSpace(parentContext?.RootExecutionId) ? rootExecutionId : parentContext!.RootExecutionId!;
    var childDepth = (parentContext?.Depth ?? 0) + 1;
    var childInvocationIndex = 0;
    // Origin of this firing: the queue run that launched it, propagated through nesting (FR-018).
    // Non-empty ⇒ a self-reschedule action can schedule into that run; empty ⇒ no-op success (FR-011).
    var originatingQueueId = parentContext?.OriginatingQueueId;
    // When this firing was itself produced by a self-reschedule, carry the originating action id so
    // the extra firing is attributable in the execution log (feature 065, FR-014).
    var selfRescheduleOriginActionId = parentContext?.SelfRescheduleOriginActionId;

    var res = await _runner.ExecuteAsync(
      sequenceId,
      async commandId => {
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
          await _commandExecutor.ForceExecuteAsync(sessionId, commandId, childContext, ct).ConfigureAwait(false);
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
      },
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
      return Task.FromResult(DispatchConnectToGame(action));
    }

    if (string.Equals(action.Type, ActionTypes.EnsureGameRunning, StringComparison.OrdinalIgnoreCase)) {
      return DispatchEnsureGameRunningAsync(sessionId, ct);
    }

    return DispatchPrimitiveInputAsync(action, sessionId, ct);
  }

  /// <summary>
  /// Handles a <c>connect-to-game</c> sequence step by starting (or restarting) a session for
  /// the game/device named in the step parameters — the same operation as the
  /// <c>/api/sessions/start</c> endpoint. Returns a <c>failed</c> outcome — which fails the
  /// step and the sequence — when parameters are missing or the session cannot be started.
  /// </summary>
  private ActionDispatchResult DispatchConnectToGame(SequenceActionPayload action) {
    if (!TryGetString(action.Parameters, "gameId", out var gameId)
        || !TryGetString(action.Parameters, "adbSerial", out var adbSerial)) {
      return new ActionDispatchResult(
        "failed",
        "connect-to-game step requires 'gameId' and 'adbSerial' parameters");
    }

    try {
      var started = _sessionService.StartSession(gameId!, adbSerial!);
      return new ActionDispatchResult(
        "executed",
        $"connected to game '{gameId}' on device '{adbSerial}' (session {started.SessionId})");
    }
    catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or ArgumentException) {
      return new ActionDispatchResult(
        "failed",
        $"connect-to-game failed for game '{gameId}' on device '{adbSerial}': {ex.Message}");
    }
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
    var resolvedSessionId = sessionId;
    if (string.IsNullOrWhiteSpace(resolvedSessionId)) {
      var runningSessions = _sessionManager.ListSessions()
        .Where(s => s.Status == GameBot.Domain.Sessions.SessionStatus.Running)
        .ToList();
      if (runningSessions.Count != 1) {
        return new ActionDispatchResult(
          "failed",
          "no session available for 'ensure-game-running' step; start a session or pass a sessionId");
      }
      resolvedSessionId = runningSessions[0].Id;
    }

    var result = await _ensureGameRunning.ExecuteAsync(resolvedSessionId!, ct).ConfigureAwait(false);
    return result.IsSuccess
      ? new ActionDispatchResult("executed", "game is running in the foreground (game_running)")
      : new ActionDispatchResult("failed", $"ensure-game-running failed: {result.ReasonCode}");
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

    var resolvedSessionId = sessionId;
    if (string.IsNullOrWhiteSpace(resolvedSessionId)) {
      var runningSessions = _sessionManager.ListSessions()
        .Where(s => s.Status == GameBot.Domain.Sessions.SessionStatus.Running)
        .ToList();
      if (runningSessions.Count != 1) {
        return new ActionDispatchResult(
          "failed",
          $"no session available for '{action.Type}' step; start a session or pass a sessionId");
      }
      resolvedSessionId = runningSessions[0].Id;
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
