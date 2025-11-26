using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services;

internal sealed class CommandExecutor : ICommandExecutor {
  private readonly ICommandRepository _commands;
  private readonly IActionRepository _actions;
  private readonly ISessionManager _sessions;
  private readonly ITriggerRepository _triggers; // No change here, just context
  private readonly TriggerEvaluationService _triggerEval;
  private readonly ILogger<CommandExecutor> _logger;

  public CommandExecutor(ICommandRepository commands, IActionRepository actions, ISessionManager sessions, ITriggerRepository triggers, TriggerEvaluationService triggerEval, ILogger<CommandExecutor> logger) {
    _commands = commands;
    _actions = actions;
    _sessions = sessions;
    _triggers = triggers; // No change here, just context
    _triggerEval = triggerEval;
    _logger = logger;
  }

  public async Task<int> ForceExecuteAsync(string sessionId, string commandId, CancellationToken ct = default) {
    var session = _sessions.GetSession(sessionId) ?? throw new KeyNotFoundException("Session not found");
    if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
      throw new InvalidOperationException("not_running");

    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    return await ExecuteCommandRecursiveAsync(sessionId, commandId, visited, ct).ConfigureAwait(false);
  }

  public async Task<CommandEvaluationDecision> EvaluateAndExecuteAsync(string sessionId, string commandId, CancellationToken ct = default) {
    var session = _sessions.GetSession(sessionId) ?? throw new KeyNotFoundException("Session not found");
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
      var accepted = await ForceExecuteAsync(sessionId, commandId, ct).ConfigureAwait(false);
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
        var inputs = act.Steps.Select(a => new GameBot.Emulator.Session.InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs));
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
}

internal static partial class Log {
  [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "EvaluateAndExecute executed command {CommandId} via trigger {TriggerId} with {Accepted} accepted inputs.")]
  public static partial void TriggerExecuted(ILogger logger, string commandId, string triggerId, int accepted);

  [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "EvaluateAndExecute skipped command {CommandId} via trigger {TriggerId}. Status: {Status}. Reason: {Reason}")]
  public static partial void TriggerSkipped(ILogger logger, string commandId, string triggerId, TriggerStatus Status, string? Reason);

  [LoggerMessage(EventId = 6002, Level = LogLevel.Information, Message = "EvaluateAndExecute bypassed trigger for command {CommandId}; executed via ForceExecute with {Accepted} accepted inputs.")]
  public static partial void TriggerBypassed(ILogger logger, string commandId, int accepted);
}
