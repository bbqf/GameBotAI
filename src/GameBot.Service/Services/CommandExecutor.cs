using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Emulator.Session;
using GameBot.Domain.Triggers;
using GameBot.Domain.Services;

namespace GameBot.Service.Services;

internal sealed class CommandExecutor : ICommandExecutor
{
    private readonly ICommandRepository _commands;
    private readonly IActionRepository _actions;
    private readonly ISessionManager _sessions;
    private readonly ITriggerRepository _triggers; // No change here, just context
    private readonly TriggerEvaluationService _triggerEval;

    public CommandExecutor(ICommandRepository commands, IActionRepository actions, ISessionManager sessions, ITriggerRepository triggers, TriggerEvaluationService triggerEval)
    {
        _commands = commands;
        _actions = actions;
        _sessions = sessions;
        _triggers = triggers; // No change here, just context
        _triggerEval = triggerEval;
    }

    public async Task<int> ForceExecuteAsync(string sessionId, string commandId, CancellationToken ct = default)
    {
        var session = _sessions.GetSession(sessionId) ?? throw new KeyNotFoundException("Session not found");
        if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
            throw new InvalidOperationException("not_running");

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return await ExecuteCommandRecursiveAsync(sessionId, commandId, visited, ct).ConfigureAwait(false);
    }

    public async Task<int> EvaluateAndExecuteAsync(string sessionId, string commandId, CancellationToken ct = default)
    {
        var session = _sessions.GetSession(sessionId) ?? throw new KeyNotFoundException("Session not found");
        if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
            throw new InvalidOperationException("not_running");

        var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false);
        if (cmd is null) throw new KeyNotFoundException("Command not found");

        if (string.IsNullOrWhiteSpace(cmd.TriggerId))
        {
            return await ForceExecuteAsync(sessionId, commandId, ct).ConfigureAwait(false);
        }

        var trigger = await _triggers.GetAsync(cmd.TriggerId!, ct).ConfigureAwait(false);
        if (trigger is null) throw new KeyNotFoundException("Trigger not found");

        var res = _triggerEval.Evaluate(trigger, DateTimeOffset.UtcNow);
        if (res.Status == TriggerStatus.Satisfied)
        {
            // Update trigger last fired to respect cooldown semantics next time
            trigger.LastResult = res;
            trigger.LastEvaluatedAt = res.EvaluatedAt;
            trigger.LastFiredAt = res.EvaluatedAt;
            // Persist trigger state so cooldown is enforced on subsequent evaluations
            await _triggers.UpsertAsync(trigger, ct).ConfigureAwait(false);
            // Fire
            return await ForceExecuteAsync(sessionId, commandId, ct).ConfigureAwait(false);
        }

        // Not satisfied: do not execute
        return 0;
    }

    private async Task<int> ExecuteCommandRecursiveAsync(string sessionId, string commandId, HashSet<string> visited, CancellationToken ct)
    {
        if (!visited.Add(commandId))
            throw new InvalidOperationException("command_cycle_detected");

        var cmd = await _commands.GetAsync(commandId, ct).ConfigureAwait(false);
        if (cmd is null) throw new KeyNotFoundException("Command not found");

        var totalAccepted = 0;
        foreach (var step in cmd.Steps.OrderBy(s => s.Order))
        {
            if (step.Type == CommandStepType.Action)
            {
                var act = await _actions.GetAsync(step.TargetId, ct).ConfigureAwait(false);
                if (act is null) throw new KeyNotFoundException("Action not found");
                if (act.Steps.Count == 0) continue;
                var inputs = act.Steps.Select(a => new GameBot.Emulator.Session.InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs));
                var accepted = await _sessions.SendInputsAsync(sessionId, inputs, ct).ConfigureAwait(false);
                totalAccepted += accepted;
            }
            else
            {
                totalAccepted += await ExecuteCommandRecursiveAsync(sessionId, step.TargetId, visited, ct).ConfigureAwait(false);
            }
        }

        visited.Remove(commandId);
        return totalAccepted;
    }
}
