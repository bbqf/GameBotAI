using GameBot.Domain.Commands;
using GameBot.Domain.Config;
using GameBot.Domain.Parameters;
using GameBot.Domain.Logging;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging;
using GameBot.Service.Services;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.StepThrough;
using GameBot.Domain.Actions;
using System.Globalization;

namespace GameBot.Service.Services;

internal sealed class CommandExecutor : ICommandExecutor {
  private readonly ICommandRepository _commands;
  private readonly ISessionManager _sessions;
  private readonly ITriggerRepository _triggers; // No change here, just context
  private readonly TriggerEvaluationService _triggerEval;
  private readonly ILogger<CommandExecutor> _logger;
  private readonly GameBot.Domain.Triggers.Evaluators.IReferenceImageStore? _images;
  // Feature 097: alternates of a reference image also count as a match for it. Null in fallback wiring.
  private readonly GameBot.Domain.Images.IImageAlternatesRepository? _alternates;
  private readonly GameBot.Domain.Triggers.Evaluators.IScreenSource? _screen;
  // Feature 079: when the executor knows which session it is acting for, detection reads that
  // session's frames explicitly instead of relying on the ambient/singleton screen source.
  private readonly GameBot.Domain.Triggers.Evaluators.IScreenSourceFactory? _screenFactory;
  private readonly GameBot.Domain.Vision.ITemplateMatcher? _matcher;
  private readonly ISessionContextCache _sessionCache;
  private readonly IExecutionLogService? _executionLogService;
  private readonly AppConfig _appConfig;
  private readonly IEnsureGameRunningActionHandler? _ensureGameRunning;
  private readonly IEnsureEmulatorRunningActionHandler? _ensureEmulatorRunning;
  private readonly IGameReadinessProbe? _gameReadiness;
  private readonly Func<CommandStep, string?> _previewRule;

  public CommandExecutor(ICommandRepository commands, ISessionManager sessions, ITriggerRepository triggers, TriggerEvaluationService triggerEval, ILogger<CommandExecutor> logger, GameBot.Domain.Triggers.Evaluators.IReferenceImageStore images, GameBot.Domain.Triggers.Evaluators.IScreenSource screen, GameBot.Domain.Vision.ITemplateMatcher matcher, ISessionContextCache sessionCache, AppConfig appConfig, IExecutionLogService? executionLogService = null, IEnsureGameRunningActionHandler? ensureGameRunning = null, IEnsureEmulatorRunningActionHandler? ensureEmulatorRunning = null, IGameReadinessProbe? gameReadiness = null, GameBot.Domain.Triggers.Evaluators.IScreenSourceFactory? screenFactory = null, GameBot.Domain.Images.IImageAlternatesRepository? alternates = null, Func<CommandStep, string?>? previewRule = null) {
    _commands = commands;
    _sessions = sessions;
    _triggers = triggers;
    _triggerEval = triggerEval;
    _logger = logger;
    _images = images;
    _screen = screen;
    _screenFactory = screenFactory;
    _matcher = matcher;
    _sessionCache = sessionCache;
    _appConfig = appConfig;
    _executionLogService = executionLogService;
    _ensureGameRunning = ensureGameRunning;
    _ensureEmulatorRunning = ensureEmulatorRunning;
    _gameReadiness = gameReadiness;
    _alternates = alternates;
    _previewRule = previewRule ?? DefaultPreviewRule;
  }

  // Fallback constructor for environments without detection services registered (non-Windows or tests)
  public CommandExecutor(ICommandRepository commands, ISessionManager sessions, ITriggerRepository triggers, TriggerEvaluationService triggerEval, ILogger<CommandExecutor> logger, ISessionContextCache sessionCache, IExecutionLogService? executionLogService = null, IEnsureGameRunningActionHandler? ensureGameRunning = null, IEnsureEmulatorRunningActionHandler? ensureEmulatorRunning = null, IGameReadinessProbe? gameReadiness = null, Func<CommandStep, string?>? previewRule = null) {
    _commands = commands;
    _sessions = sessions;
    _triggers = triggers;
    _triggerEval = triggerEval;
    _logger = logger;
    _images = null;
    _screen = null;
    _screenFactory = null;
    _matcher = null;
    _sessionCache = sessionCache;
    _appConfig = new AppConfig();
    _executionLogService = executionLogService;
    _ensureGameRunning = ensureGameRunning;
    _ensureEmulatorRunning = ensureEmulatorRunning;
    _gameReadiness = gameReadiness;
    _previewRule = previewRule ?? DefaultPreviewRule;
  }

  // Feature 127: the rule that decides which command steps a step-through previews. The default is the
  // shared rule table. A test can pass its own rule.
  private static string? DefaultPreviewRule(CommandStep step)
    => PreviewEffectRules.TryDescribe(step, out var effect) ? effect : null;

  /// <summary>
  /// Returns the screen source detection should read for <paramref name="sessionId"/> (feature 079).
  /// </summary>
  /// <remarks>
  /// Prefers an explicitly session-bound source so concurrent queue runs cannot observe one another's
  /// device. Falls back to the injected singleton when no factory is registered (stub/test mode) or
  /// no session is known, where the ambient device context still applies.
  /// </remarks>
  private GameBot.Domain.Triggers.Evaluators.IScreenSource? ResolveScreenSource(string? sessionId) {
    if (_screenFactory is not null && !string.IsNullOrWhiteSpace(sessionId)) {
      return _screenFactory.ForSession(sessionId);
    }
    return _screen;
  }

  public Task<int> ForceExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default)
    => ForceExecuteAsync(sessionId, commandId, new ExecutionLogContext { Depth = 0 }, ct);

  public async Task<int> ForceExecuteAsync(string? sessionId, string commandId, ExecutionLogContext context, CancellationToken ct = default) {
    var result = await ForceExecuteDetailedAsync(sessionId, commandId, context, ParameterScope.Empty, ct).ConfigureAwait(false);
    return result.Accepted;
  }

  public async Task<int> ForceExecuteAsync(string? sessionId, string commandId, ExecutionLogContext context, ParameterScope scope, CancellationToken ct = default) {
    var result = await ForceExecuteDetailedAsync(sessionId, commandId, context, scope, ct).ConfigureAwait(false);
    return result.Accepted;
  }

  public Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, CancellationToken ct = default)
    => ForceExecuteDetailedAsync(sessionId, commandId, new ExecutionLogContext { Depth = 0 }, ParameterScope.Empty, ct);

  public Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, ExecutionLogContext context, CancellationToken ct = default)
    => ForceExecuteDetailedAsync(sessionId, commandId, context, ParameterScope.Empty, ct);

  /// <summary>
  /// Force-executes a command, resolving every step against <paramref name="scope"/> before dispatch.
  /// </summary>
  /// <param name="sessionId">Session to execute against, or null to use the cached one.</param>
  /// <param name="commandId">Command to execute.</param>
  /// <param name="context">Execution-log context.</param>
  /// <param name="scope">Parameter scope in effect at the call site.</param>
  /// <param name="ct">Cancellation token.</param>
  public Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, ExecutionLogContext context, ParameterScope scope, CancellationToken ct = default)
    => ForceExecuteDetailedAsync(sessionId, commandId, context, scope, null, ct);

  public async Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, ExecutionLogContext context, ParameterScope scope, ExecutionOptions? options, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(scope);
    var resolvedSessionId = await ResolveSessionIdAsync(sessionId, commandId, ct).ConfigureAwait(false);
    var session = _sessions.GetSession(resolvedSessionId) ?? throw new KeyNotFoundException("Session not found");
    if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
      throw new InvalidOperationException("not_running");

    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var stepOutcomes = new List<PrimitiveTapStepOutcome>();
    var previewed = new List<string>();
    var accepted = await ExecuteCommandRecursiveAsync(resolvedSessionId, commandId, visited, stepOutcomes, scope, options, previewed, ct).ConfigureAwait(false);
    if (_executionLogService is not null) {
      var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false);
      var cmdName = cmd?.Name ?? commandId;
      var hasStepFailures = stepOutcomes.Any(o => !string.Equals(o.Status, "executed", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(o.Status, PreviewEffectRules.PreviewedStatus, StringComparison.OrdinalIgnoreCase));
      var status = accepted > 0 && !hasStepFailures ? "success" : "failure";
      await _executionLogService.LogCommandExecutionAsync(
        commandId,
        cmdName,
        status,
        stepOutcomes,
        context,
        ct).ConfigureAwait(false);
    }
    return new CommandForceExecutionResult(accepted, stepOutcomes, options?.PreviewEffects == true ? previewed : null);
  }

  public async Task<CommandEvaluationDecision> EvaluateAndExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default) {
    var result = await EvaluateAndExecuteDetailedAsync(sessionId, commandId, ct).ConfigureAwait(false);
    return new CommandEvaluationDecision(result.Accepted, result.TriggerStatus, result.Reason);
  }

  public async Task<CommandEvaluationExecutionResult> EvaluateAndExecuteDetailedAsync(string? sessionId, string commandId, CancellationToken ct = default) {
    var resolvedSessionId = await ResolveSessionIdAsync(sessionId, commandId, ct).ConfigureAwait(false);
    var session = _sessions.GetSession(resolvedSessionId) ?? throw new KeyNotFoundException("Session not found");
    if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
      throw new InvalidOperationException("not_running");

    var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false);
    if (cmd is null) throw new KeyNotFoundException("Command not found");

    if (string.IsNullOrWhiteSpace(cmd.TriggerId)) {
      // No trigger configured: do not execute. Return pending/unsatisfied.
      if (_executionLogService is not null) {
        await _executionLogService.LogCommandExecutionAsync(
          commandId,
          cmd.Name,
          "failure",
          Array.Empty<PrimitiveTapStepOutcome>(),
          new ExecutionLogContext { Depth = 0 },
          ct).ConfigureAwait(false);
      }
      return new CommandEvaluationExecutionResult(0, TriggerStatus.Pending, "no_trigger_configured", Array.Empty<PrimitiveTapStepOutcome>());
    }

    var trigger = await _triggers.GetAsync(cmd.TriggerId!, ct).ConfigureAwait(false);
    if (trigger is null) throw new KeyNotFoundException("Trigger not found");

    var res = _triggerEval.Evaluate(trigger, DateTimeOffset.UtcNow);
    trigger.LastResult = res;
    trigger.LastEvaluatedAt = res.EvaluatedAt;

    if (res.Status == TriggerStatus.Satisfied) {
      trigger.LastFiredAt = res.EvaluatedAt;
      await _triggers.UpsertAsync(trigger, ct).ConfigureAwait(false);
      var forceResult = await ForceExecuteDetailedAsync(resolvedSessionId, commandId, ct).ConfigureAwait(false);
      Log.TriggerExecuted(_logger, commandId, trigger.Id, forceResult.Accepted);
      return new CommandEvaluationExecutionResult(forceResult.Accepted, TriggerStatus.Satisfied, res.Reason, forceResult.StepOutcomes);
    }

    await _triggers.UpsertAsync(trigger, ct).ConfigureAwait(false);
    Log.TriggerSkipped(_logger, commandId, trigger.Id, res.Status, res.Reason);
    if (_executionLogService is not null) {
      await _executionLogService.LogCommandExecutionAsync(
        commandId,
        cmd.Name,
        "failure",
        Array.Empty<PrimitiveTapStepOutcome>(),
        new ExecutionLogContext { Depth = 0 },
        ct).ConfigureAwait(false);
    }
    return new CommandEvaluationExecutionResult(0, res.Status, res.Reason, Array.Empty<PrimitiveTapStepOutcome>());
  }

  public Task<CommandForceExecutionResult> ForceExecuteStepAsync(string? sessionId, CommandStep step, CancellationToken ct = default)
    => ForceExecuteStepAsync(sessionId, step, null, ct);

  public async Task<CommandForceExecutionResult> ForceExecuteStepAsync(string? sessionId, CommandStep step, TimeSpan? timeout, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(step);
    string resolvedSessionId;
    if (!string.IsNullOrWhiteSpace(sessionId)) {
      resolvedSessionId = sessionId;
    }
    else {
      var runningSessions = _sessions.ListSessions()
        .Where(s => s.Status == GameBot.Domain.Sessions.SessionStatus.Running)
        .ToList();
      if (runningSessions.Count == 1) {
        resolvedSessionId = runningSessions[0].Id;
      }
      else if (runningSessions.Count > 1) {
        // Feature 079 (FR-007): several devices are live and the caller named none. Say so instead of
        // acting on an arbitrary one. The sentinel stays "missing_session_context" so existing API
        // mappings and callers keep working; the ambiguity is carried in the inner exception's text.
        throw new InvalidOperationException(
          "missing_session_context",
          new InvalidOperationException(SessionResolver.Ambiguous(runningSessions.Count, "force-execute")));
      }
      else {
        throw new InvalidOperationException("missing_session_context");
      }
    }

    var session = _sessions.GetSession(resolvedSessionId) ?? throw new KeyNotFoundException("Session not found");
    if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
      throw new InvalidOperationException("not_running");

    // Feature 112 (issue #222): the executor owns the time limit, so that it can tell a timeout from a
    // cancellation by the caller. Each call from here on writes exactly one execution-log entry.
    using var timeoutCts = new CancellationTokenSource();
    if (timeout is { } limit) {
      timeoutCts.CancelAfter(limit);
    }
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
    var startedAtUtc = DateTimeOffset.UtcNow;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var stepType = step.Type.ToString();

    int accepted;
    PrimitiveTapStepOutcome outcome;
    try {
      (accepted, outcome) = await ExecuteOneStepAsync(resolvedSessionId, step, null, linkedCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested) {
      await WriteStepLogAsync(resolvedSessionId, stepType, new PrimitiveTapStepOutcome(step.Order, "timeout", "step_execution_timeout", null, null), 0, startedAtUtc, stopwatch).ConfigureAwait(false);
      throw new TimeoutException("step_execution_timeout");
    }
    catch (OperationCanceledException) {
      await WriteStepLogAsync(resolvedSessionId, stepType, new PrimitiveTapStepOutcome(step.Order, "cancelled", "step_execution_cancelled", null, null), 0, startedAtUtc, stopwatch).ConfigureAwait(false);
      throw;
    }
    catch (Exception ex) {
      await WriteStepLogAsync(resolvedSessionId, stepType, new PrimitiveTapStepOutcome(step.Order, "failed", $"step_exception: {ex.GetType().Name}", null, null), 0, startedAtUtc, stopwatch).ConfigureAwait(false);
      throw;
    }

    await WriteStepLogAsync(resolvedSessionId, stepType, outcome, accepted, startedAtUtc, stopwatch).ConfigureAwait(false);
    return new CommandForceExecutionResult(accepted, new[] { outcome });
  }

  /// <summary>
  /// Feature 112: writes the execution-log entry of one single step call. The write uses no token of the
  /// call, so a timeout or a cancellation does not stop it. A failure of the write gives only a warning.
  /// </summary>
  private async Task WriteStepLogAsync(string sessionId, string stepType, PrimitiveTapStepOutcome outcome, int accepted, DateTimeOffset startedAtUtc, System.Diagnostics.Stopwatch stopwatch) {
    if (_executionLogService is null) return;
    try {
      var record = new StepExecutionLogRecord(sessionId, stepType, outcome, accepted, startedAtUtc, stopwatch.ElapsedMilliseconds);
      await _executionLogService.LogStepExecutionAsync(record, CancellationToken.None).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // A failure of the diagnostic log must not change the result of an input that the service already sent.
    catch (Exception ex) {
#pragma warning restore CA1031
      Log.StepLogWriteFailed(_logger, ex, sessionId);
    }
  }

  private async Task<int> ExecuteCommandRecursiveAsync(string sessionId, string commandId, HashSet<string> visited, List<PrimitiveTapStepOutcome> stepOutcomes, ParameterScope scope, ExecutionOptions? options, List<string> previewed, CancellationToken ct) {
    if (!visited.Add(commandId))
      throw new InvalidOperationException("command_cycle_detected");

    var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false);
    if (cmd is null) throw new KeyNotFoundException("Command not found");
    if (cmd.Steps.Count == 0)
      throw new InvalidOperationException("command_has_no_steps");

    // This command's own declarations join the scope, so their defaults cover any name no caller
    // supplied. Bindings still outrank defaults regardless of nesting depth (feature 078, FR-009).
    var commandScope = cmd.Parameters.Count > 0
      ? scope.Child(ParameterScopeLayers.Command, null, cmd.Parameters)
      : scope;

    var totalAccepted = 0;
    foreach (var step in cmd.Steps.OrderBy(s => s.Order)) {
      if (step.Type == CommandStepType.Command) {
        // A nested command invocation pushes its own bindings before recursing. A {{name}}
        // placeholder in a binding value resolves against the scope of this command (feature 115).
        // When a placeholder does not resolve, the nested command does not run, and the next step
        // of this command runs.
        var nestedScope = commandScope;
        if (step.ParameterBindings is { Count: > 0 }) {
          if (!commandScope.TryBindChild(ParameterScopeLayers.Command, step.ParameterBindings, out var bound, out var bindingError)) {
            stepOutcomes.Add(new PrimitiveTapStepOutcome(
              step.Order,
              "skipped_parameter_unresolved",
              bindingError.ToMessage(step.Order.ToString(System.Globalization.CultureInfo.InvariantCulture)),
              null,
              null,
              StepType: step.Type.ToString()));
            continue;
          }

          nestedScope = bound;
        }

        totalAccepted += await ExecuteCommandRecursiveAsync(sessionId, step.TargetId, visited, stepOutcomes, nestedScope, options, previewed, ct).ConfigureAwait(false);
        continue;
      }

      // Resolve immediately before dispatch. A failure here dispatches nothing to the device, so an
      // unresolved placeholder can never reach a real emulator (feature 078, FR-017/FR-018).
      if (!CommandStepResolver.TryResolve(step, commandScope, out var effectiveStep, out var resolutionError, out var usedParameters)) {
        stepOutcomes.Add(new PrimitiveTapStepOutcome(
          step.Order,
          "skipped_parameter_unresolved",
          resolutionError!.ToMessage(step.Order.ToString(System.Globalization.CultureInfo.InvariantCulture)),
          null,
          null,
          StepType: step.Type.ToString()));
        continue;
      }

      // Feature 127: a step-through shows the effect of a step with an outside effect and does not run it.
      if (options?.PreviewEffects == true && _previewRule(effectiveStep) is { } previewedEffect) {
        previewed.Add(previewedEffect);
        stepOutcomes.Add(new PrimitiveTapStepOutcome(
          step.Order,
          PreviewEffectRules.PreviewedStatus,
          previewedEffect,
          null,
          null,
          StepType: step.Type.ToString()));
        continue;
      }

      var (accepted, outcome) = await ExecuteOneStepAsync(sessionId, effectiveStep, cmd.Detection, ct).ConfigureAwait(false);
      totalAccepted += accepted;
      stepOutcomes.Add(usedParameters.Count > 0
        ? outcome with { ResolvedParameters = usedParameters }
        : outcome);
    }

    visited.Remove(commandId);
    return totalAccepted;
  }

  private async Task<(int Accepted, PrimitiveTapStepOutcome Outcome)> ExecuteOneStepAsync(
      string sessionId,
      CommandStep step,
      DetectionTarget? commandLevelDetection,
      CancellationToken ct) {
    if (step.Type == CommandStepType.WaitForImage) {
      return (0, await ExecuteWaitForImageStepAsync(step, sessionId, ct).ConfigureAwait(false));
    }

    if (step.Type == CommandStepType.EnsureGameRunning) {
      var result = _ensureGameRunning is not null
        ? await _ensureGameRunning.ExecuteAsync(sessionId, ct).ConfigureAwait(false)
        : new EnsureGameRunningActionResult(EnsureGameRunningOutcome.PlatformUnsupported);

      var readiness = step.EnsureGameRunning?.ReadinessImage;
      var hasReadinessGate = _gameReadiness is not null
        && readiness is not null
        && !string.IsNullOrWhiteSpace(readiness.ReferenceImageId);

      // Without a readiness image (or a probe to run it), preserve the legacy behavior:
      // success iff the game was already the foreground app.
      if (!hasReadinessGate) {
        return result.IsSuccess
          ? (1, new PrimitiveTapStepOutcome(step.Order, "executed", result.ReasonCode, null, null, StepType: "ensure-game-running"))
          : (0, new PrimitiveTapStepOutcome(step.Order, result.ReasonCode, result.ReasonCode, null, null, StepType: "ensure-game-running"));
      }

      // The game/session could not be resolved (or the platform is unsupported) â€” there is nothing
      // to wait for, so surface the handler failure directly instead of spinning on the screen.
      if (result.Outcome is EnsureGameRunningOutcome.NoQueueContext
          or EnsureGameRunningOutcome.NoLinkedGame
          or EnsureGameRunningOutcome.NoPackageName
          or EnsureGameRunningOutcome.PlatformUnsupported) {
        return (0, new PrimitiveTapStepOutcome(step.Order, result.ReasonCode, result.ReasonCode, null, null, StepType: "ensure-game-running"));
      }

      // Handler has (best-effort) launched the game if it was not foreground. Now wait for it to
      // actually reach the ready screen before letting the queue proceed.
      var readinessTimeoutMs = Math.Max(0, step.EnsureGameRunning!.ReadinessTimeoutMs);
      var readinessResult = await _gameReadiness!.WaitUntilReadyAsync(readiness!, readinessTimeoutMs, sessionId, ct).ConfigureAwait(false);
      return readinessResult.Ready
        ? (1, new PrimitiveTapStepOutcome(step.Order, "executed", "game_ready", null, null, StepType: "ensure-game-running", TimeoutMs: readinessTimeoutMs, EffectiveTimeoutMs: readinessTimeoutMs, ReferenceImageId: readiness!.ReferenceImageId, ImageLoadStatus: readinessResult.ImageLoadStatus))
        : (0, new PrimitiveTapStepOutcome(step.Order, "readiness_timeout", "readiness_timeout", null, null, StepType: "ensure-game-running", TimeoutMs: readinessTimeoutMs, EffectiveTimeoutMs: readinessTimeoutMs, ReferenceImageId: readiness!.ReferenceImageId, ImageLoadStatus: readinessResult.ImageLoadStatus));
    }

    if (step.Type == CommandStepType.EnsureEmulatorRunning) {
      var cfg = step.EnsureEmulatorRunning;
      if (cfg is null || string.IsNullOrWhiteSpace(cfg.AdbSerial)
          || (string.IsNullOrWhiteSpace(cfg.InstanceName) && cfg.InstanceIndex is null)) {
        return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "ensure_emulator_running_missing_config", null, null, StepType: "ensure-emulator-running"));
      }
      var emulatorArgs = new EnsureEmulatorRunningArgs {
        InstanceName = cfg.InstanceName,
        InstanceIndex = cfg.InstanceIndex,
        AdbSerial = cfg.AdbSerial
      };
      var emulatorResult = _ensureEmulatorRunning is not null
        ? await _ensureEmulatorRunning.ExecuteAsync(emulatorArgs, ct).ConfigureAwait(false)
        : new EnsureEmulatorRunningActionResult(EnsureEmulatorRunningOutcome.PlatformUnsupported);
      return emulatorResult.IsSuccess || emulatorResult.IsUnsupported
        ? (1, new PrimitiveTapStepOutcome(step.Order, "executed", emulatorResult.ReasonCode, null, null, StepType: "ensure-emulator-running"))
        : (0, new PrimitiveTapStepOutcome(step.Order, emulatorResult.ReasonCode, emulatorResult.ReasonCode, null, null, StepType: "ensure-emulator-running"));
    }

    if (step.Type == CommandStepType.GoToHomeScreen) {
      // Feature 069: press Android HOME (keycode 3) so the device returns to the home/main screen.
      // The game is left running in the background (HOME does not force-stop it). Reuses the key
      // input path, which stubs success on non-Windows/non-ADB sessions.
      const int androidKeyCodeHome = 3;
      var homeArgs = new Dictionary<string, object> { ["keyCode"] = androidKeyCodeHome };
      var homeAction = new GameBot.Emulator.Session.InputAction("key", homeArgs, null, null);
      var homeAccepted = await _sessions.SendInputsAsync(sessionId, new[] { homeAction }, ct).ConfigureAwait(false);
      return homeAccepted > 0
        ? (homeAccepted, new PrimitiveTapStepOutcome(step.Order, "executed", null, null, null, StepType: "go-to-home-screen"))
        : (0, new PrimitiveTapStepOutcome(step.Order, "not_accepted", "go_to_home_screen_not_accepted", null, null, StepType: "go-to-home-screen"));
    }

    if (step.Type == CommandStepType.KeyInput) {
      if (step.KeyInput is null) {
        return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "key_input_missing_config", null, null, StepType: "key"));
      }
      var keyArgs = new Dictionary<string, object> { ["key"] = step.KeyInput.Key };
      var keyAction = new GameBot.Emulator.Session.InputAction("key", keyArgs, null, null);
      var keyAccepted = await _sessions.SendInputsAsync(sessionId, new[] { keyAction }, ct).ConfigureAwait(false);
      return (keyAccepted, new PrimitiveTapStepOutcome(step.Order, "executed", null, null, null, StepType: "key"));
    }

    if (step.Type == CommandStepType.Swipe) {
      if (step.Swipe is null) {
        return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "swipe_missing_config", null, null, StepType: "swipe"));
      }
      var swipeArgs = new Dictionary<string, object> {
        ["x1"] = step.Swipe.StartX,
        ["y1"] = step.Swipe.StartY,
        ["x2"] = step.Swipe.EndX,
        ["y2"] = step.Swipe.EndY
      };
      if (step.Swipe.DurationMs.HasValue) {
        swipeArgs["durationMs"] = step.Swipe.DurationMs.Value;
      }
      var targetSwipe = new PrimitiveSwipePoints(
        new PrimitiveTapResolvedPoint(step.Swipe.StartX, step.Swipe.StartY),
        new PrimitiveTapResolvedPoint(step.Swipe.EndX, step.Swipe.EndY));
      var swipeAction = new GameBot.Emulator.Session.InputAction("swipe", swipeArgs, null, null);
      var swipeAccepted = await _sessions.SendInputsAsync(sessionId, new[] { swipeAction }, ct).ConfigureAwait(false);
      // The session manager applies tap-point jitter by mutating the args in place; read the
      // values back so the outcome reports the coordinates actually sent to the device.
      var executedSwipe = new PrimitiveSwipePoints(
        new PrimitiveTapResolvedPoint(
          Convert.ToInt32(swipeArgs["x1"], CultureInfo.InvariantCulture),
          Convert.ToInt32(swipeArgs["y1"], CultureInfo.InvariantCulture)),
        new PrimitiveTapResolvedPoint(
          Convert.ToInt32(swipeArgs["x2"], CultureInfo.InvariantCulture),
          Convert.ToInt32(swipeArgs["y2"], CultureInfo.InvariantCulture)));
      return (swipeAccepted, new PrimitiveTapStepOutcome(step.Order, "executed", null, null, null, StepType: "swipe",
        TargetSwipe: targetSwipe, ExecutedSwipe: executedSwipe));
    }

    if (step.Type == CommandStepType.PrimitiveTap) {
      // Backward-compatible fallback: older commands may keep detection at command level.
      var primitiveDetection = step.PrimitiveTap?.DetectionTarget ?? commandLevelDetection;
      if (primitiveDetection is null) {
        Log.DetectionSkip(_logger, "primitive_tap_missing_detection");
        return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "primitive_tap_missing_detection", null, null));
      }

      if (!OperatingSystem.IsWindows()) {
        Log.DetectionSkip(_logger, "primitive_tap_detection_windows_only");
        return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "primitive_tap_detection_windows_only", null, null));
      }

      var cancelCycleTracker = 0;
      var dispatch = new TapDispatchState();
      try {
        var screenSrc = ResolveScreenSource(sessionId);
        var images = _images;
        var matcher = _matcher;
        if (screenSrc is null || images is null || matcher is null) {
          Log.DetectionSkip(_logger, "services_unavailable");
          return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "services_unavailable", null, null));
        }

        if (!GameBot.Domain.Images.ReferenceImageSetLoader.TryLoad(images, _alternates, primitiveDetection.ReferenceImageId, out var templateSet) || templateSet is null) {
          Log.DetectionSkip(_logger, "template_not_found");
          return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "template_not_found", null, null));
        }

        templateSet.LogMissingAlternates(_logger);
        var baseWaitMs = _appConfig.CaptureIntervalMs;
        var retryCount = _appConfig.TapRetryCount;
        var progression = _appConfig.TapRetryProgression;
        var currentWaitMs = (double)baseWaitMs;

        Log.TapRetryWaiting(_logger, step.Order, baseWaitMs, 0);
        await Task.Delay(baseWaitMs, ct).ConfigureAwait(false);

        var tapOutcomes = new List<PrimitiveTapStepOutcome>();
        var detected = TryDetectAndTap(screenSrc, templateSet, primitiveDetection, matcher, step, sessionId, tapOutcomes, dispatch, 0, ct);

        if (!detected) {
          Log.TapRetryNotDetected(_logger, step.Order, 0);

          for (int retry = 0; retry < retryCount; retry++) {
            cancelCycleTracker = retry + 1;
            var waitMs = (int)currentWaitMs;
            Log.TapRetryWaiting(_logger, step.Order, waitMs, cancelCycleTracker);
            await Task.Delay(waitMs, ct).ConfigureAwait(false);
            currentWaitMs *= progression;

            detected = TryDetectAndTap(screenSrc, templateSet, primitiveDetection, matcher, step, sessionId, tapOutcomes, dispatch, cancelCycleTracker, ct);
            if (detected) {
              Log.TapRetryDetected(_logger, step.Order, cancelCycleTracker);
              break;
            }

            Log.TapRetryNotDetected(_logger, step.Order, cancelCycleTracker);
          }

          if (!detected) {
            Log.TapRetryExhausted(_logger, step.Order, retryCount);
            return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_detection_failed", $"detection_failed_after_{retryCount}_retries", null, null));
          }
        }

        return (dispatch.Accepted, tapOutcomes[0]);
      }
      catch (OperationCanceledException ex) {
        // Feature 112 (issue #222): a "not executed" outcome must mean that no input went to the device.
        // When the dispatch started, the outcome tells that the input was sent or that the result is not known.
        if (dispatch.Started) {
          Log.TapDispatchProblem(_logger, ex, step.Order, dispatch.Completed);
          return DispatchedOutcome(step.Order, dispatch, cancelled: true);
        }
        Log.TapRetryCancelled(_logger, step.Order, cancelCycleTracker);
        return (0, new PrimitiveTapStepOutcome(step.Order, "cancelled", $"cancelled_during_retry_{cancelCycleTracker}", null, null));
      }
      catch (Exception ex) {
        if (dispatch.Started) {
          Log.TapDispatchProblem(_logger, ex, step.Order, dispatch.Completed);
          return DispatchedOutcome(step.Order, dispatch, cancelled: false);
        }
        Log.DetectionError(_logger, ex);
        return (0, new PrimitiveTapStepOutcome(step.Order, "skipped_detection_failed", "primitive_tap_exception", null, null));
      }
    }

    throw new InvalidOperationException($"Step type {step.Type} cannot be executed as a standalone step");
  }

  private async Task<PrimitiveTapStepOutcome> ExecuteWaitForImageStepAsync(CommandStep step, string sessionId, CancellationToken ct) {
    var waitConfig = step.WaitForImage ?? new WaitForImageConfig();
    var timeoutMs = Math.Max(0, waitConfig.TimeoutMs);
    var detectionTarget = waitConfig.DetectionTarget;
    var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);

    if (detectionTarget is null || string.IsNullOrWhiteSpace(detectionTarget.ReferenceImageId)) {
      // A wait with no image to look for is not a search that failed — it is a plain delay, the only
      // way to express "pause here" in a command. Waiting it out IS the step succeeding, so it must
      // report "executed": a command's status is failure if ANY step is not executed, so reporting a
      // timeout here marked every command ending in a delay as failed. That mislabelled a great many
      // commands that had done exactly what they were told, and buried the genuine failures among them.
      await WaitForTimeoutAsync(deadline, ct).ConfigureAwait(false);
      return new PrimitiveTapStepOutcome(
        step.Order,
        "executed",
        "delay_elapsed",
        null,
        null,
        StepType: "waitForImage",
        TimeoutMs: timeoutMs,
        EffectiveTimeoutMs: timeoutMs);
    }

    if (!OperatingSystem.IsWindows()) {
      await WaitForTimeoutAsync(deadline, ct).ConfigureAwait(false);
      return new PrimitiveTapStepOutcome(
        step.Order,
        "completed_image_unavailable",
        "image_unavailable",
        null,
        null,
        StepType: "waitForImage",
        TimeoutMs: timeoutMs,
        EffectiveTimeoutMs: timeoutMs,
        ReferenceImageId: detectionTarget.ReferenceImageId,
        ImageLoadStatus: "unavailable",
        ConfiguredConfidence: detectionTarget.Confidence);
    }

    var screenSrc = ResolveScreenSource(sessionId);
    var images = _images;
    var matcher = _matcher;
    if (screenSrc is null || images is null || matcher is null) {
      await WaitForTimeoutAsync(deadline, ct).ConfigureAwait(false);
      return new PrimitiveTapStepOutcome(
        step.Order,
        "completed_image_unavailable",
        "image_unavailable",
        null,
        null,
        StepType: "waitForImage",
        TimeoutMs: timeoutMs,
        EffectiveTimeoutMs: timeoutMs,
        ReferenceImageId: detectionTarget.ReferenceImageId,
        ImageLoadStatus: "unavailable",
        ConfiguredConfidence: detectionTarget.Confidence);
    }

    if (!GameBot.Domain.Images.ReferenceImageSetLoader.TryLoad(images, _alternates, detectionTarget.ReferenceImageId, out var templateSet) || templateSet is null) {
      await WaitForTimeoutAsync(deadline, ct).ConfigureAwait(false);
      return new PrimitiveTapStepOutcome(
        step.Order,
        "completed_image_unavailable",
        "image_unavailable",
        null,
        null,
        StepType: "waitForImage",
        TimeoutMs: timeoutMs,
        EffectiveTimeoutMs: timeoutMs,
        ReferenceImageId: detectionTarget.ReferenceImageId,
        ImageLoadStatus: "missing",
        ConfiguredConfidence: detectionTarget.Confidence);
    }

    templateSet.LogMissingAlternates(_logger);
    if (TryDetectImage(screenSrc, templateSet, detectionTarget, matcher, out var resolvedPoint, out var detectionConfidence)) {
      return new PrimitiveTapStepOutcome(
        step.Order,
        "executed",
        "image_detected",
        resolvedPoint,
        detectionConfidence,
        StepType: "waitForImage",
        TimeoutMs: timeoutMs,
        EffectiveTimeoutMs: timeoutMs,
        ReferenceImageId: detectionTarget.ReferenceImageId,
        ImageLoadStatus: "loaded",
        ConfiguredConfidence: detectionTarget.Confidence);
    }

    while (DateTimeOffset.UtcNow < deadline) {
      var remaining = deadline - DateTimeOffset.UtcNow;
      if (remaining <= TimeSpan.Zero) {
        break;
      }

      var pollMs = Math.Max(1, Math.Min(_appConfig.CaptureIntervalMs, (int)Math.Ceiling(remaining.TotalMilliseconds)));
      await Task.Delay(pollMs, ct).ConfigureAwait(false);

      if (TryDetectImage(screenSrc, templateSet, detectionTarget, matcher, out resolvedPoint, out detectionConfidence)) {
        return new PrimitiveTapStepOutcome(
          step.Order,
          "executed",
          "image_detected",
          resolvedPoint,
          detectionConfidence,
          StepType: "waitForImage",
          TimeoutMs: timeoutMs,
          EffectiveTimeoutMs: timeoutMs,
          ReferenceImageId: detectionTarget.ReferenceImageId,
          ImageLoadStatus: "loaded",
          ConfiguredConfidence: detectionTarget.Confidence);
      }
    }

    return new PrimitiveTapStepOutcome(
      step.Order,
      "completed_timeout",
      "timeout_elapsed",
      null,
      null,
      StepType: "waitForImage",
      TimeoutMs: timeoutMs,
      EffectiveTimeoutMs: timeoutMs,
      ReferenceImageId: detectionTarget.ReferenceImageId,
        ImageLoadStatus: "loaded",
        ConfiguredConfidence: detectionTarget.Confidence);
  }

  private static async Task WaitForTimeoutAsync(DateTimeOffset deadline, CancellationToken ct) {
    var remaining = deadline - DateTimeOffset.UtcNow;
    if (remaining > TimeSpan.Zero) {
      await Task.Delay(remaining, ct).ConfigureAwait(false);
    }
  }

  private bool TryDetectImage(
    GameBot.Domain.Triggers.Evaluators.IScreenSource screenSrc,
    GameBot.Domain.Images.ReferenceImageSet templateSet,
    DetectionTarget detectionTarget,
    GameBot.Domain.Vision.ITemplateMatcher matcher,
    out PrimitiveTapResolvedPoint? resolvedPoint,
    out double? detectionConfidence)
    => ImageDetectionHelper.TryDetect(screenSrc, templateSet, detectionTarget, matcher, out resolvedPoint, out detectionConfidence, _logger);

  /// <summary>
  /// Attempts a single screenshot-fetch â†’ template-match â†’ coordinate-resolve â†’ tap cycle.
  /// Returns true if detection succeeded and the tap was sent; false otherwise.
  /// On success, appends the outcome to <paramref name="stepOutcomes"/>.
  /// Feature 112: records the dispatch in <paramref name="dispatch"/>, so that the caller can tell
  /// the truth about the input when an error or a cancellation occurs after the dispatch started.
  /// </summary>
  private bool TryDetectAndTap(
    GameBot.Domain.Triggers.Evaluators.IScreenSource screenSrc,
    GameBot.Domain.Images.ReferenceImageSet templateSet,
    DetectionTarget primitiveDetection,
    GameBot.Domain.Vision.ITemplateMatcher matcher,
    CommandStep step,
    string sessionId,
    List<PrimitiveTapStepOutcome> stepOutcomes,
    TapDispatchState dispatch,
    int retryAttempt,
    CancellationToken ct) {
    var screenshotBmp = screenSrc.GetLatestScreenshot();
    if (screenshotBmp is null) return false;

    using var screenMs = new System.IO.MemoryStream();
    screenshotBmp.Save(screenMs, System.Drawing.Imaging.ImageFormat.Png);
    using var screenMat = OpenCvSharp.Mat.FromImageData(screenMs.ToArray(), OpenCvSharp.ImreadModes.Color);
    // Alpha-preserving template decode, so a masked reference image masks here too (feature 089).
    using var templateMat = ImageDetectionHelper.ToTemplateMat(templateSet.Primary);
    // Feature 097: the named image's alternates also count as a match for it.
    using var matcherLease = templateSet.CreateMatcher(matcher, ImageDetectionHelper.ToTemplateMat, _logger);

    var adapter = new GameBot.Domain.Services.ActionExecutionAdapter(matcherLease.Matcher);
    var primitiveAction = new GameBot.Domain.Actions.InputAction {
      Type = "tap",
      Args = new Dictionary<string, object> { ["x"] = 0, ["y"] = 0 }
    };

    var ok = adapter.TryApplyDetectionCoordinates(
      primitiveAction,
      primitiveDetection,
      screenMat,
      templateMat,
      primitiveDetection.Confidence,
      out var err,
      DetectionSelectionStrategy.HighestConfidence);

    if (!ok || err is not null) return false;

    if (!primitiveAction.Args.TryGetValue("x", out var xVal) || !primitiveAction.Args.TryGetValue("y", out var yVal))
      return false;

    var x = Convert.ToInt32(xVal, CultureInfo.InvariantCulture);
    var y = Convert.ToInt32(yVal, CultureInfo.InvariantCulture);
    if (x < 0 || y < 0 || x >= screenshotBmp.Width || y >= screenshotBmp.Height)
      return false;

    // Use swipe-to-same-point so the tap has a hold duration; more reliable on slow emulators.
    // Feature 111: a step with a hold duration more than 0 holds the point for that duration.
    var holdMs = PrimitiveTapInput.EffectiveHoldMs(step.PrimitiveTap?.HoldMs);
    var sessionInput = PrimitiveTapInput.Create(x, y, holdMs);
    var tapArgs = sessionInput.Args;

    // Feature 112: read the confidence before the dispatch, so that less code runs after it.
    var detectionConfidence = primitiveAction.Args.TryGetValue("confidence", out var confidenceVal)
      ? Convert.ToDouble(confidenceVal, CultureInfo.InvariantCulture)
      : (double?)null;

    var resolvedPoint = new PrimitiveTapResolvedPoint(x, y);
    dispatch.ResolvedPoint = resolvedPoint;
    dispatch.DetectionConfidence = detectionConfidence;
    dispatch.HoldMs = holdMs;
    dispatch.Started = true;
    dispatch.Accepted = _sessions.SendInputsAsync(sessionId, new[] { sessionInput }, ct).GetAwaiter().GetResult();
    dispatch.Completed = true;

    // The session manager applies tap-point jitter by mutating the args in place; read the
    // values back so the outcome reports the coordinates actually sent to the device.
    var executedX = Convert.ToInt32(tapArgs["x1"], CultureInfo.InvariantCulture);
    var executedY = Convert.ToInt32(tapArgs["y1"], CultureInfo.InvariantCulture);
    dispatch.ExecutedPoint = new PrimitiveTapResolvedPoint(executedX, executedY);

    var reason = retryAttempt > 0 ? $"detected_after_{retryAttempt}_retries" : null;
    stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "executed", reason, resolvedPoint, detectionConfidence,
      ExecutedPoint: dispatch.ExecutedPoint, HoldMs: holdMs));
    return true;
  }

  /// <summary>
  /// Feature 112 (issue #222): makes the outcome of a PrimitiveTap step when an error or a cancellation
  /// occurred after the dispatch started. After a completed dispatch, the status is <c>executed</c> with the
  /// accepted count of the session. During the dispatch, the status is <c>dispatch_unknown</c>, because the
  /// device can have the input.
  /// </summary>
  private static (int Accepted, PrimitiveTapStepOutcome Outcome) DispatchedOutcome(int stepOrder, TapDispatchState dispatch, bool cancelled) {
    if (dispatch.Completed) {
      return (dispatch.Accepted, new PrimitiveTapStepOutcome(
        stepOrder,
        "executed",
        cancelled ? "executed_then_cancelled" : "executed_then_error",
        dispatch.ResolvedPoint,
        dispatch.DetectionConfidence,
        ExecutedPoint: dispatch.ExecutedPoint,
        HoldMs: dispatch.HoldMs));
    }

    return (0, new PrimitiveTapStepOutcome(
      stepOrder,
      "dispatch_unknown",
      cancelled ? "dispatch_cancelled" : "dispatch_error",
      dispatch.ResolvedPoint,
      dispatch.DetectionConfidence,
      HoldMs: dispatch.HoldMs));
  }

  /// <summary>
  /// Feature 112 (issue #222): the state of the tap dispatch of one PrimitiveTap step.
  /// <see cref="Started"/> is true immediately before the call to the session.
  /// <see cref="Completed"/> is true when the session returned.
  /// </summary>
  private sealed class TapDispatchState {
    public bool Started { get; set; }
    public bool Completed { get; set; }
    public int Accepted { get; set; }
    public PrimitiveTapResolvedPoint? ResolvedPoint { get; set; }
    public PrimitiveTapResolvedPoint? ExecutedPoint { get; set; }
    public double? DetectionConfidence { get; set; }
    public int? HoldMs { get; set; }
  }

  private async Task<string> ResolveSessionIdAsync(string? sessionId, string commandId, CancellationToken ct) {
    if (!string.IsNullOrWhiteSpace(sessionId)) {
      Log.SessionProvided(_logger, commandId, sessionId);
      return sessionId;
    }

    var runningSessions = _sessions.ListSessions().Where(session => session.Status == GameBot.Domain.Sessions.SessionStatus.Running).ToList();
    if (runningSessions.Count == 1) {
      var resolved = runningSessions[0].Id;
      Log.SessionProvided(_logger, commandId, resolved);
      return resolved;
    }

    var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Command not found");
    if (cmd.Steps.Count > 0) {
      Log.SessionContextMissing(_logger, commandId);
    }

    // Feature 079 (FR-007): distinguish "no device at all" from "several devices, none named" while
    // keeping the "missing_session_context" sentinel every caller and API mapping already matches on.
    throw runningSessions.Count > 1
      ? new InvalidOperationException(
          "missing_session_context",
          new InvalidOperationException(SessionResolver.Ambiguous(runningSessions.Count, commandId)))
      : new InvalidOperationException("missing_session_context");
  }
}

internal static partial class Log {
  [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "EvaluateAndExecute executed command {CommandId} via trigger {TriggerId} with {Accepted} accepted inputs.")]
  public static partial void TriggerExecuted(ILogger logger, string commandId, string triggerId, int accepted);

  [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "EvaluateAndExecute skipped command {CommandId} via trigger {TriggerId}. Status: {Status}. Reason: {Reason}")]
  public static partial void TriggerSkipped(ILogger logger, string commandId, string triggerId, TriggerStatus Status, string? Reason);

  [LoggerMessage(EventId = 6002, Level = LogLevel.Information, Message = "EvaluateAndExecute bypassed trigger for command {CommandId}; executed via ForceExecute with {Accepted} accepted inputs.")]
  public static partial void TriggerBypassed(ILogger logger, string commandId, int accepted);

  [LoggerMessage(EventId = 6003, Level = LogLevel.Debug, Message = "Detection coordinate resolution skipped: {Err}")]
  public static partial void DetectionSkip(ILogger logger, string Err);

  [LoggerMessage(EventId = 6004, Level = LogLevel.Debug, Message = "Detection wiring encountered an issue; proceeding without coordinates.")]
  public static partial void DetectionError(ILogger logger, Exception ex);

  [LoggerMessage(EventId = 6005, Level = LogLevel.Debug, Message = "SessionId provided explicitly for command {CommandId} (session {SessionId}).")]
  public static partial void SessionProvided(ILogger logger, string CommandId, string SessionId);

  [LoggerMessage(EventId = 6006, Level = LogLevel.Information, Message = "Using cached session for command {CommandId}: game {GameId}, device {AdbSerial}, session {SessionId}.")]
  public static partial void SessionCacheHit(ILogger logger, string CommandId, string GameId, string AdbSerial, string SessionId);

  [LoggerMessage(EventId = 6007, Level = LogLevel.Warning, Message = "No cached session for command {CommandId}: game {GameId}, device {AdbSerial}.")]
  public static partial void SessionCacheMiss(ILogger logger, string CommandId, string GameId, string AdbSerial);

  [LoggerMessage(EventId = 6008, Level = LogLevel.Warning, Message = "No connect-to-game context found for command {CommandId} to resolve session.")]
  public static partial void SessionContextMissing(ILogger logger, string CommandId);

  [LoggerMessage(EventId = 6009, Level = LogLevel.Debug, Message = "PrimitiveTap step {StepOrder}: waiting {WaitMs}ms before retry cycle {Cycle}.")]
  public static partial void TapRetryWaiting(ILogger logger, int StepOrder, int WaitMs, int Cycle);

  [LoggerMessage(EventId = 6010, Level = LogLevel.Information, Message = "PrimitiveTap step {StepOrder}: target detected on cycle {Cycle}.")]
  public static partial void TapRetryDetected(ILogger logger, int StepOrder, int Cycle);

  [LoggerMessage(EventId = 6011, Level = LogLevel.Debug, Message = "PrimitiveTap step {StepOrder}: target not detected on cycle {Cycle}.")]
  public static partial void TapRetryNotDetected(ILogger logger, int StepOrder, int Cycle);

  [LoggerMessage(EventId = 6012, Level = LogLevel.Warning, Message = "PrimitiveTap step {StepOrder}: target not detected after {TotalCycles} retry cycles.")]
  public static partial void TapRetryExhausted(ILogger logger, int StepOrder, int TotalCycles);

  [LoggerMessage(EventId = 6013, Level = LogLevel.Information, Message = "PrimitiveTap step {StepOrder}: cancelled during retry cycle {Cycle}.")]
  public static partial void TapRetryCancelled(ILogger logger, int StepOrder, int Cycle);

  [LoggerMessage(EventId = 6014, Level = LogLevel.Warning, Message = "PrimitiveTap step {StepOrder}: an error or a cancellation occurred after the dispatch started. The session returned: {DispatchCompleted}.")]
  public static partial void TapDispatchProblem(ILogger logger, Exception ex, int StepOrder, bool DispatchCompleted);

  [LoggerMessage(EventId = 6015, Level = LogLevel.Warning, Message = "The execution-log entry for a single step on session {SessionId} was not written.")]
  public static partial void StepLogWriteFailed(ILogger logger, Exception ex, string SessionId);
}
