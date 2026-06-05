using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Triggers;
using GameBot.Service.Services.ExecutionLog;

namespace GameBot.Service.Services;

internal interface ICommandExecutor {
  Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, CancellationToken ct = default);
  /// <summary>
  /// Force-executes a command, logging it with the supplied execution context so a command
  /// invoked as part of a sequence is recorded as a linked child rather than a top-level entry.
  /// </summary>
  Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, ExecutionLogContext context, CancellationToken ct = default);
  Task<int> ForceExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<int> ForceExecuteAsync(string? sessionId, string commandId, ExecutionLogContext context, CancellationToken ct = default);
  Task<CommandEvaluationExecutionResult> EvaluateAndExecuteDetailedAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<CommandEvaluationDecision> EvaluateAndExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<CommandForceExecutionResult> ForceExecuteStepAsync(string? sessionId, GameBot.Domain.Commands.CommandStep step, CancellationToken ct = default);
}

internal sealed record CommandEvaluationDecision(int Accepted, TriggerStatus TriggerStatus, string? Reason);

internal sealed record PrimitiveTapResolvedPoint(int X, int Y);

internal sealed record PrimitiveTapStepOutcome(
  int StepOrder,
  string Status,
  string? Reason,
  PrimitiveTapResolvedPoint? ResolvedPoint,
  double? DetectionConfidence,
  string? StepType = null,
  int? TimeoutMs = null,
  int? EffectiveTimeoutMs = null,
  string? ReferenceImageId = null,
  string? ImageLoadStatus = null,
  double? ConfiguredConfidence = null);

internal sealed record CommandForceExecutionResult(int Accepted, IReadOnlyList<PrimitiveTapStepOutcome> StepOutcomes);

internal sealed record CommandEvaluationExecutionResult(
  int Accepted,
  TriggerStatus TriggerStatus,
  string? Reason,
  IReadOnlyList<PrimitiveTapStepOutcome> StepOutcomes);
