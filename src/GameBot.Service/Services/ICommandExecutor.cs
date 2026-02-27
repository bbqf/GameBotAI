using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Triggers;

namespace GameBot.Service.Services;

internal interface ICommandExecutor {
  Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<int> ForceExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<CommandEvaluationExecutionResult> EvaluateAndExecuteDetailedAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<CommandEvaluationDecision> EvaluateAndExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default);
}

internal sealed record CommandEvaluationDecision(int Accepted, TriggerStatus TriggerStatus, string? Reason);

internal sealed record PrimitiveTapResolvedPoint(int X, int Y);

internal sealed record PrimitiveTapStepOutcome(
  int StepOrder,
  string Status,
  string? Reason,
  PrimitiveTapResolvedPoint? ResolvedPoint,
  double? DetectionConfidence);

internal sealed record CommandForceExecutionResult(int Accepted, IReadOnlyList<PrimitiveTapStepOutcome> StepOutcomes);

internal sealed record CommandEvaluationExecutionResult(
  int Accepted,
  TriggerStatus TriggerStatus,
  string? Reason,
  IReadOnlyList<PrimitiveTapStepOutcome> StepOutcomes);
