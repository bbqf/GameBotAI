using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Emulator.Session;

namespace GameBot.Service.Services;

internal sealed class CommandExecutor : ICommandExecutor
{
    private readonly ICommandRepository _commands;
    private readonly IActionRepository _actions;
    private readonly ISessionManager _sessions;

    public CommandExecutor(ICommandRepository commands, IActionRepository actions, ISessionManager sessions)
    {
        _commands = commands;
        _actions = actions;
        _sessions = sessions;
    }

    public async Task<int> ForceExecuteAsync(string sessionId, string commandId, CancellationToken ct = default)
    {
        var session = _sessions.GetSession(sessionId) ?? throw new KeyNotFoundException("Session not found");
        if (session.Status != GameBot.Domain.Sessions.SessionStatus.Running)
            throw new InvalidOperationException("not_running");

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return await ExecuteCommandRecursiveAsync(sessionId, commandId, visited, ct).ConfigureAwait(false);
    }

    public Task<int> EvaluateAndExecuteAsync(string sessionId, string commandId, CancellationToken ct = default)
    {
        // Placeholder: trigger evaluation gating will be wired after trigger decoupling is finalized.
        // For now, behave like force-execute.
        return ForceExecuteAsync(sessionId, commandId, ct);
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
                var inputs = act.Steps.Select(a => new InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs));
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
