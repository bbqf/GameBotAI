using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Logging;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.StepThrough;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>
/// The parts of <see cref="SequenceExecutionService"/> that both a real run and a step-through use
/// (feature 127): the gate, the condition evaluation, and the command dispatch. A real run and a
/// step-through call the same methods.
/// </summary>
internal sealed partial class SequenceExecutionService {
  public StepperDependencies CreateStepWiring(StepWiringRequest request) {
    ArgumentNullException.ThrowIfNull(request);
    var invocation = 0;
    // An empty root id means that the log entry could not be opened. The commands then have no parent.
    var rootId = string.IsNullOrWhiteSpace(request.RootExecutionId) ? null : request.RootExecutionId;

    async Task<GameBot.Domain.Services.CommandDispatchOutcome> Dispatch(string commandId, ParameterScope scope) {
      var childContext = new ExecutionLogContext {
        ParentExecutionId = rootId,
        RootExecutionId = rootId,
        Depth = rootId is null ? 0 : 1,
        SequenceIndex = ++invocation,
        SequenceId = request.SequenceId,
        SequenceLabel = request.SequenceName,
        Origin = ExecutionOrigins.StepThrough
      };
      return await DispatchCommandCoreAsync(
        request.SessionId, commandId, scope, childContext, new ExecutionOptions(PreviewEffects: true), request.Token).ConfigureAwait(false);
    }

    return new StepperDependencies {
      ExecuteCommandAsync = async (commandId, scope) => await Dispatch(commandId, scope).ConfigureAwait(false),
      CommandDispatcher = Dispatch,
      GateEvaluator = EvaluateGate,
      ConditionEvaluator = (condition, token) => EvaluateConditionAsync(condition, false, token),
      // A device action runs as in a real run. The two actions with an outside effect never reach this
      // callback: the stepper previews them (FR-015).
      ActionDispatcher = (action, token) => DispatchActionAsync(
        action, request.SequenceId, null, request.SessionId, ParameterScope.Empty, request.RootExecutionId, token),
      PreviewServiceAction = (action, _) => Task.FromResult(
        StepThroughActionPreview.Describe(action, _selfRescheduleCoordinator, DateTimeOffset.Now))
    };
  }

  // Temporary evaluator for integration tests:
  // TargetId "always" => gate passes; "never" => gate fails
  private static Task<bool> EvaluateGate(GameBot.Domain.Commands.SequenceStep step, CancellationToken token) {
    if (step.Gate == null) return Task.FromResult(true);
    var tid = step.Gate.TargetId ?? string.Empty;
    if (string.Equals(tid, "always", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
    if (string.Equals(tid, "never", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(false);
    return Task.FromResult(true);
  }

  private Task<bool> EvaluateConditionAsync(GameBot.Domain.Commands.Blocks.Condition cond, bool dryRun, CancellationToken token) {
    // Feature 082 (FR-015): under dryRun, an image or text condition never reads live capture state.
    // It gives false, as an ordinary false condition does. A dry run therefore needs no session for it.
    // A commandOutcome condition does not change: SequenceRunner resolves it directly.
    if (dryRun && (string.Equals(cond.Source, "image", StringComparison.OrdinalIgnoreCase)
        || string.Equals(cond.Source, "text", StringComparison.OrdinalIgnoreCase))) {
      return Task.FromResult(false);
    }
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
  }

  /// <summary>
  /// Invokes one command step and reports whether its input reached the device. The runner can then
  /// record the true outcome and not assume "executed" when nothing threw. A real run and a step-through
  /// use this method. With <paramref name="options"/> in preview mode, the outcome also lists the effects
  /// that the command previewed.
  /// </summary>
  private async Task<GameBot.Domain.Services.CommandDispatchOutcome> DispatchCommandCoreAsync(
      string? sessionId,
      string commandId,
      ParameterScope stepScope,
      ExecutionLogContext childContext,
      ExecutionOptions? options,
      CancellationToken ct) {
    try {
      var detailed = options is null
        ? await _commandExecutor.ForceExecuteDetailedAsync(sessionId, commandId, childContext, stepScope, ct).ConfigureAwait(false)
        : await _commandExecutor.ForceExecuteDetailedAsync(sessionId, commandId, childContext, stepScope, options, ct).ConfigureAwait(false);
      var outcome = ClassifyDispatch(detailed, ct);
      return detailed.PreviewedEffects is { Count: > 0 } effects
        ? outcome with { PreviewedEffects = effects }
        : outcome;
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
