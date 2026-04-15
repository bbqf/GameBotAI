using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Config;
using GameBot.Domain.Logging;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using System.Globalization;

namespace GameBot.Service.Services;

internal sealed class CommandExecutor : ICommandExecutor {
  private readonly ICommandRepository _commands;
  private readonly IActionRepository _actions;
  private readonly ISessionManager _sessions;
  private readonly ITriggerRepository _triggers; // No change here, just context
  private readonly TriggerEvaluationService _triggerEval;
  private readonly ILogger<CommandExecutor> _logger;
  private readonly GameBot.Domain.Triggers.Evaluators.IReferenceImageStore? _images;
  private readonly GameBot.Domain.Triggers.Evaluators.IScreenSource? _screen;
  private readonly GameBot.Domain.Vision.ITemplateMatcher? _matcher;
  private readonly ISessionContextCache _sessionCache;
  private readonly IExecutionLogService? _executionLogService;
  private readonly AppConfig _appConfig;

  public CommandExecutor(ICommandRepository commands, IActionRepository actions, ISessionManager sessions, ITriggerRepository triggers, TriggerEvaluationService triggerEval, ILogger<CommandExecutor> logger, GameBot.Domain.Triggers.Evaluators.IReferenceImageStore images, GameBot.Domain.Triggers.Evaluators.IScreenSource screen, GameBot.Domain.Vision.ITemplateMatcher matcher, ISessionContextCache sessionCache, AppConfig appConfig, IExecutionLogService? executionLogService = null) {
    _commands = commands;
    _actions = actions;
    _sessions = sessions;
    _triggers = triggers; // No change here, just context
    _triggerEval = triggerEval;
    _logger = logger;
    _images = images;
    _screen = screen;
    _matcher = matcher;
    _sessionCache = sessionCache;
    _appConfig = appConfig;
    _executionLogService = executionLogService;
  }

  // Fallback constructor for environments without detection services registered (non-Windows or tests)
  public CommandExecutor(ICommandRepository commands, IActionRepository actions, ISessionManager sessions, ITriggerRepository triggers, TriggerEvaluationService triggerEval, ILogger<CommandExecutor> logger, ISessionContextCache sessionCache, IExecutionLogService? executionLogService = null) {
    _commands = commands;
    _actions = actions;
    _sessions = sessions;
    _triggers = triggers;
    _triggerEval = triggerEval;
    _logger = logger;
    _images = null;
    _screen = null;
    _matcher = null;
    _sessionCache = sessionCache;
    _appConfig = new AppConfig();
    _executionLogService = executionLogService;
  }

  public async Task<int> ForceExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default) {
    var result = await ForceExecuteDetailedAsync(sessionId, commandId, ct).ConfigureAwait(false);
    return result.Accepted;
  }

  public async Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, CancellationToken ct = default) {
    var resolvedSessionId = await ResolveSessionIdAsync(sessionId, commandId, ct).ConfigureAwait(false);
    var session = _sessions.GetSession(resolvedSessionId) ?? throw new KeyNotFoundException("Session not found");
    if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
      throw new InvalidOperationException("not_running");

    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var stepOutcomes = new List<PrimitiveTapStepOutcome>();
    var accepted = await ExecuteCommandRecursiveAsync(resolvedSessionId, commandId, visited, stepOutcomes, ct).ConfigureAwait(false);
    if (_executionLogService is not null) {
      var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false);
      var cmdName = cmd?.Name ?? commandId;
      var status = stepOutcomes.Any(o => !string.Equals(o.Status, "executed", StringComparison.OrdinalIgnoreCase)) ? "failure" : "success";
      await _executionLogService.LogCommandExecutionAsync(
        commandId,
        cmdName,
        status,
        stepOutcomes,
        new ExecutionLogContext { Depth = 0 },
        ct).ConfigureAwait(false);
    }
    return new CommandForceExecutionResult(accepted, stepOutcomes);
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

  private async Task<int> ExecuteCommandRecursiveAsync(string sessionId, string commandId, HashSet<string> visited, List<PrimitiveTapStepOutcome> stepOutcomes, CancellationToken ct) {
    if (!visited.Add(commandId))
      throw new InvalidOperationException("command_cycle_detected");

    var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false);
    if (cmd is null) throw new KeyNotFoundException("Command not found");

    var totalAccepted = 0;
    foreach (var step in cmd.Steps.OrderBy(s => s.Order)) {
      if (step.Type == CommandStepType.Action) {
        var act = await _actions.GetAsync(step.TargetId, ct).ConfigureAwait(false);
        if (act is null) throw new KeyNotFoundException("Action not found");
        if (act.Steps.Count == 0) continue;
        var inputs = act.Steps.Select(a => new GameBot.Emulator.Session.InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs)).ToList();
        // If the command includes a detection target, attempt to resolve coordinates for tap actions
        if (cmd.Detection is not null && OperatingSystem.IsWindows()) {
          try {
#nullable disable
            var detection = cmd.Detection!;
            var screenSrc = _screen;
            var images = _images;
            var matcher = _matcher;
            if (screenSrc is null || images is null || matcher is null) { Log.DetectionSkip(_logger, "services_unavailable"); }
            var screenshotBmp = screenSrc?.GetLatestScreenshot();
            if (screenshotBmp is null) { /* no screenshot; skip */ }
            else if (images.TryGet(detection.ReferenceImageId, out var templateBmp)) {
              var tbmp = templateBmp!;
              using var template = new System.Drawing.Bitmap(tbmp);
              using var screenMs = new System.IO.MemoryStream();
              using var templateMs = new System.IO.MemoryStream();
              screenshotBmp.Save(screenMs, System.Drawing.Imaging.ImageFormat.Png);
              template.Save(templateMs, System.Drawing.Imaging.ImageFormat.Png);
              var screenMat = OpenCvSharp.Mat.FromImageData(screenMs.ToArray(), OpenCvSharp.ImreadModes.Color);
              var templateMat = OpenCvSharp.Mat.FromImageData(templateMs.ToArray(), OpenCvSharp.ImreadModes.Color);
              if (matcher is null) { Log.DetectionSkip(_logger, "matcher_unavailable"); }
              else {
                var adapter = new GameBot.Domain.Services.ActionExecutionAdapter(matcher);
                foreach (var inp in inputs) {
                  var ok = adapter.TryApplyDetectionCoordinates(
                    new GameBot.Domain.Actions.InputAction { Type = inp.Type, Args = inp.Args, DelayMs = inp.DelayMs, DurationMs = inp.DurationMs },
                    detection,
                    screenMat,
                    templateMat,
                    detection.Confidence,
                    out var err);
                  if (!ok && err is not null) { Log.DetectionSkip(_logger, err); }
                }
              }
              screenMat.Dispose();
              templateMat.Dispose();
#nullable restore
            }
          }
          catch (Exception ex) {
            Log.DetectionError(_logger, ex);
          }
        }
        var accepted = await _sessions.SendInputsAsync(sessionId, inputs, ct).ConfigureAwait(false);
        totalAccepted += accepted;
      }
      else if (step.Type == CommandStepType.PrimitiveTap) {
        var primitiveDetection = step.PrimitiveTap?.DetectionTarget;
        if (primitiveDetection is null) {
          Log.DetectionSkip(_logger, "primitive_tap_missing_detection");
          stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "primitive_tap_missing_detection", null, null));
          continue;
        }

        if (!OperatingSystem.IsWindows()) {
          Log.DetectionSkip(_logger, "primitive_tap_detection_windows_only");
          stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "primitive_tap_detection_windows_only", null, null));
          continue;
        }

        var cancelCycleTracker = 0;
        try {
          var screenSrc = _screen;
          var images = _images;
          var matcher = _matcher;
          if (screenSrc is null || images is null || matcher is null) {
            Log.DetectionSkip(_logger, "services_unavailable");
            stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "services_unavailable", null, null));
            continue;
          }

          // Template lookup before loop — template doesn't change between retries (R-005)
          if (!images.TryGet(primitiveDetection.ReferenceImageId, out var templateBmp) || templateBmp is null) {
            Log.DetectionSkip(_logger, "template_not_found");
            stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "skipped_invalid_config", "template_not_found", null, null));
            continue;
          }

          var baseWaitMs = _appConfig.CaptureIntervalMs;
          var retryCount = _appConfig.TapRetryCount;
          var progression = _appConfig.TapRetryProgression;
          var currentWaitMs = (double)baseWaitMs;
          var detected = false;

          // Initial wait + detection check (FR-001)
          Log.TapRetryWaiting(_logger, step.Order, baseWaitMs, 0);
          await Task.Delay(baseWaitMs, ct).ConfigureAwait(false);

          detected = TryDetectAndTap(screenSrc, templateBmp, primitiveDetection, matcher, step, sessionId, stepOutcomes, ref totalAccepted, 0, ct);

          if (!detected) {
            Log.TapRetryNotDetected(_logger, step.Order, 0);

            // Retry loop (FR-002, FR-003)
            for (int retry = 0; retry < retryCount; retry++) {
              cancelCycleTracker = retry + 1;
              var waitMs = (int)currentWaitMs;
              Log.TapRetryWaiting(_logger, step.Order, waitMs, cancelCycleTracker);
              await Task.Delay(waitMs, ct).ConfigureAwait(false);
              currentWaitMs *= progression; // progression applied after each retry wait

              detected = TryDetectAndTap(screenSrc, templateBmp, primitiveDetection, matcher, step, sessionId, stepOutcomes, ref totalAccepted, cancelCycleTracker, ct);
              if (detected) {
                Log.TapRetryDetected(_logger, step.Order, cancelCycleTracker);
                break;
              }
              Log.TapRetryNotDetected(_logger, step.Order, cancelCycleTracker);
            }

            if (!detected) {
              Log.TapRetryExhausted(_logger, step.Order, retryCount);
              stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "skipped_detection_failed", $"detection_failed_after_{retryCount}_retries", null, null));
            }
          }
        }
        catch (OperationCanceledException) {
          Log.TapRetryCancelled(_logger, step.Order, cancelCycleTracker);
          stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "cancelled", $"cancelled_during_retry_{cancelCycleTracker}", null, null));
        }
        catch (Exception ex) {
          Log.DetectionError(_logger, ex);
          stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "skipped_detection_failed", "primitive_tap_exception", null, null));
        }
      }
      else {
        totalAccepted += await ExecuteCommandRecursiveAsync(sessionId, step.TargetId, visited, stepOutcomes, ct).ConfigureAwait(false);
      }
    }

    visited.Remove(commandId);
    return totalAccepted;
  }

  /// <summary>
  /// Attempts a single screenshot-fetch → template-match → coordinate-resolve → tap cycle.
  /// Returns true if detection succeeded and the tap was sent; false otherwise.
  /// On success, appends the outcome to <paramref name="stepOutcomes"/> and increments <paramref name="totalAccepted"/>.
  /// </summary>
  private bool TryDetectAndTap(
    GameBot.Domain.Triggers.Evaluators.IScreenSource screenSrc,
    System.Drawing.Bitmap templateBmp,
    DetectionTarget primitiveDetection,
    GameBot.Domain.Vision.ITemplateMatcher matcher,
    CommandStep step,
    string sessionId,
    List<PrimitiveTapStepOutcome> stepOutcomes,
    ref int totalAccepted,
    int retryAttempt,
    CancellationToken ct)
  {
    var screenshotBmp = screenSrc.GetLatestScreenshot();
    if (screenshotBmp is null) return false;

    using var template = new System.Drawing.Bitmap(templateBmp);
    using var screenMs = new System.IO.MemoryStream();
    using var templateMs = new System.IO.MemoryStream();
    screenshotBmp.Save(screenMs, System.Drawing.Imaging.ImageFormat.Png);
    template.Save(templateMs, System.Drawing.Imaging.ImageFormat.Png);
    using var screenMat = OpenCvSharp.Mat.FromImageData(screenMs.ToArray(), OpenCvSharp.ImreadModes.Color);
    using var templateMat = OpenCvSharp.Mat.FromImageData(templateMs.ToArray(), OpenCvSharp.ImreadModes.Color);

    var adapter = new GameBot.Domain.Services.ActionExecutionAdapter(matcher);
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

    var sessionInput = new GameBot.Emulator.Session.InputAction("tap", new Dictionary<string, object>(primitiveAction.Args), null, null);
    var accepted = _sessions.SendInputsAsync(sessionId, new[] { sessionInput }, ct).GetAwaiter().GetResult();
    totalAccepted += accepted;

    var detectionConfidence = primitiveAction.Args.TryGetValue("confidence", out var confidenceVal)
      ? Convert.ToDouble(confidenceVal, CultureInfo.InvariantCulture)
      : (double?)null;

    var reason = retryAttempt > 0 ? $"detected_after_{retryAttempt}_retries" : null;
    stepOutcomes.Add(new PrimitiveTapStepOutcome(step.Order, "executed", reason, new PrimitiveTapResolvedPoint(x, y), detectionConfidence));
    return true;
  }

  private async Task<string> ResolveSessionIdAsync(string? sessionId, string commandId, CancellationToken ct) {
    if (!string.IsNullOrWhiteSpace(sessionId)) {
      Log.SessionProvided(_logger, commandId, sessionId);
      return sessionId;
    }

    var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Command not found");
    // Find first connect-to-game action referenced by this command
    foreach (var step in cmd.Steps.OrderBy(s => s.Order)) {
      if (step.Type != CommandStepType.Action) continue;
      var act = await _actions.GetAsync(step.TargetId, ct).ConfigureAwait(false);
      if (act is null) continue;
      foreach (var input in act.Steps) {
        if (ConnectToGameArgs.TryFrom(input, act.GameId, out var args)) {
          var cached = _sessionCache.GetSessionId(args.GameId, args.AdbSerial);
          if (string.IsNullOrWhiteSpace(cached)) {
            Log.SessionCacheMiss(_logger, commandId, args.GameId, args.AdbSerial);
            throw new KeyNotFoundException("cached_session_not_found");
          }
          Log.SessionCacheHit(_logger, commandId, args.GameId, args.AdbSerial, cached!);
          return cached!;
        }
      }
    }

    Log.SessionContextMissing(_logger, commandId);
    throw new InvalidOperationException("missing_session_context");
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
}
