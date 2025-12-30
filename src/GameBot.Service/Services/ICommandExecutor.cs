using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Triggers;

namespace GameBot.Service.Services;

internal interface ICommandExecutor {
  Task<int> ForceExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<CommandEvaluationDecision> EvaluateAndExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default);
}

internal sealed record CommandEvaluationDecision(int Accepted, TriggerStatus TriggerStatus, string? Reason);
