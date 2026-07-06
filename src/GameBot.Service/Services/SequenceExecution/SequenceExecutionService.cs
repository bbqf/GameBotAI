using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Images;
using GameBot.Domain.Logging;
using GameBot.Domain.Services;
using GameBot.Service.Services.Conditions;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;

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

  public SequenceExecutionService(
    SequenceRunner runner,
    TriggerEvaluationService evalSvc,
    IImageVisibleConditionAdapter imageVisibleConditionAdapter,
    IImageRepository imageRepository,
    IExecutionLogService executionLogService,
    ICommandRepository commandRepository,
    ISequenceRepository sequenceRepository,
    ICommandExecutor commandExecutor,
    ISelfRescheduleCoordinator selfRescheduleCoordinator) {
    _runner = runner;
    _evalSvc = evalSvc;
    _imageVisibleConditionAdapter = imageVisibleConditionAdapter;
    _imageRepository = imageRepository;
    _executionLogService = executionLogService;
    _commandRepository = commandRepository;
    _sequenceRepository = sequenceRepository;
    _commandExecutor = commandExecutor;
    _selfRescheduleCoordinator = selfRescheduleCoordinator;
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
        catch (KeyNotFoundException) {
          // Command not found in repository — step uses a primitive action type (e.g. tap)
          // or references a non-existent command; treat as completed.
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
      actionDispatcher: (action, token) => Task.FromResult(DispatchSelfReschedule(action, sequenceId, originatingQueueId))
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
      string? originatingQueueId) {
    if (string.IsNullOrWhiteSpace(originatingQueueId)) {
      return new ActionDispatchResult("noop", "no originating queue, no reschedule performed");
    }

    if (!SelfReschedulePayload.TryRead(action, out var payload, out var parseError) || payload is null) {
      return new ActionDispatchResult("noop", $"self-reschedule not performed: {parseError}");
    }

    var schedule = _selfRescheduleCoordinator.ScheduleSelf(
      originatingQueueId!,
      sequenceId,
      payload.Option,
      payload.TimerTimeOfDay,
      payload.TimerRelativeOffset);

    if (schedule.Outcome == SelfRescheduleOutcome.NotRunning) {
      return new ActionDispatchResult("noop", "originating queue run no longer active; no reschedule performed");
    }

    return new ActionDispatchResult(
      "scheduled",
      $"rescheduled this sequence (option {schedule.Option}, {schedule.ResolvedTiming}); applies to the current run only");
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
