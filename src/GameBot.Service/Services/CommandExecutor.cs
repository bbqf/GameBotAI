using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging;
using GameBot.Service.Services;

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

  public CommandExecutor(ICommandRepository commands, IActionRepository actions, ISessionManager sessions, ITriggerRepository triggers, TriggerEvaluationService triggerEval, ILogger<CommandExecutor> logger, GameBot.Domain.Triggers.Evaluators.IReferenceImageStore images, GameBot.Domain.Triggers.Evaluators.IScreenSource screen, GameBot.Domain.Vision.ITemplateMatcher matcher, ISessionContextCache sessionCache) {
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
  }

  // Fallback constructor for environments without detection services registered (non-Windows or tests)
  public CommandExecutor(ICommandRepository commands, IActionRepository actions, ISessionManager sessions, ITriggerRepository triggers, TriggerEvaluationService triggerEval, ILogger<CommandExecutor> logger, ISessionContextCache sessionCache) {
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
  }

  public async Task<int> ForceExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default) {
    var resolvedSessionId = await ResolveSessionIdAsync(sessionId, commandId, ct).ConfigureAwait(false);
    var session = _sessions.GetSession(resolvedSessionId) ?? throw new KeyNotFoundException("Session not found");
    if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
      throw new InvalidOperationException("not_running");

    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    return await ExecuteCommandRecursiveAsync(resolvedSessionId, commandId, visited, ct).ConfigureAwait(false);
  }

  public async Task<CommandEvaluationDecision> EvaluateAndExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default) {
    var resolvedSessionId = await ResolveSessionIdAsync(sessionId, commandId, ct).ConfigureAwait(false);
    var session = _sessions.GetSession(resolvedSessionId) ?? throw new KeyNotFoundException("Session not found");
    if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
      throw new InvalidOperationException("not_running");

    var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false);
    if (cmd is null) throw new KeyNotFoundException("Command not found");

    if (string.IsNullOrWhiteSpace(cmd.TriggerId)) {
      // No trigger configured: do not execute. Return pending/unsatisfied.
      return new CommandEvaluationDecision(0, TriggerStatus.Pending, "no_trigger_configured");
    }

    var trigger = await _triggers.GetAsync(cmd.TriggerId!, ct).ConfigureAwait(false);
    if (trigger is null) throw new KeyNotFoundException("Trigger not found");

    var res = _triggerEval.Evaluate(trigger, DateTimeOffset.UtcNow);
    trigger.LastResult = res;
    trigger.LastEvaluatedAt = res.EvaluatedAt;

    if (res.Status == TriggerStatus.Satisfied) {
      trigger.LastFiredAt = res.EvaluatedAt;
      await _triggers.UpsertAsync(trigger, ct).ConfigureAwait(false);
      var accepted = await ForceExecuteAsync(resolvedSessionId, commandId, ct).ConfigureAwait(false);
      Log.TriggerExecuted(_logger, commandId, trigger.Id, accepted);
      return new CommandEvaluationDecision(accepted, TriggerStatus.Satisfied, res.Reason);
    }

    await _triggers.UpsertAsync(trigger, ct).ConfigureAwait(false);
    Log.TriggerSkipped(_logger, commandId, trigger.Id, res.Status, res.Reason);
    return new CommandEvaluationDecision(0, res.Status, res.Reason);
  }

  private async Task<int> ExecuteCommandRecursiveAsync(string sessionId, string commandId, HashSet<string> visited, CancellationToken ct) {
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
      else {
        totalAccepted += await ExecuteCommandRecursiveAsync(sessionId, step.TargetId, visited, ct).ConfigureAwait(false);
      }
    }

    visited.Remove(commandId);
    return totalAccepted;
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
}
