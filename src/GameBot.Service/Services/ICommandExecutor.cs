using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Parameters;
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

  /// <summary>
  /// Force-executes a command with a parameter scope (feature 078). Each step is resolved against
  /// <paramref name="scope"/> immediately before dispatch; a step whose parameters cannot be resolved
  /// fails without dispatching anything to the device.
  /// </summary>
  /// <param name="sessionId">Session to execute against, or null to use the cached one.</param>
  /// <param name="commandId">Command to execute.</param>
  /// <param name="context">Execution-log context linking this run to its parent.</param>
  /// <param name="scope">Scope in effect at the invoking sequence step.</param>
  /// <param name="ct">Cancellation token.</param>
  Task<int> ForceExecuteAsync(string? sessionId, string commandId, ExecutionLogContext context, ParameterScope scope, CancellationToken ct = default);
  Task<CommandEvaluationExecutionResult> EvaluateAndExecuteDetailedAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<CommandEvaluationDecision> EvaluateAndExecuteAsync(string? sessionId, string commandId, CancellationToken ct = default);
  Task<CommandForceExecutionResult> ForceExecuteStepAsync(string? sessionId, GameBot.Domain.Commands.CommandStep step, CancellationToken ct = default);
}

internal sealed record CommandEvaluationDecision(int Accepted, TriggerStatus TriggerStatus, string? Reason);

internal sealed record PrimitiveTapResolvedPoint(int X, int Y);

/// <summary>Start/end point pair for a swipe step (pre- or post-jitter).</summary>
internal sealed record PrimitiveSwipePoints(PrimitiveTapResolvedPoint Start, PrimitiveTapResolvedPoint End);

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
  double? ConfiguredConfidence = null,
  PrimitiveTapResolvedPoint? ExecutedPoint = null,
  PrimitiveSwipePoints? TargetSwipe = null,
  PrimitiveSwipePoints? ExecutedSwipe = null,
  IReadOnlyList<ResolvedParameter>? ResolvedParameters = null);

internal sealed record CommandForceExecutionResult(int Accepted, IReadOnlyList<PrimitiveTapStepOutcome> StepOutcomes);

internal sealed record CommandEvaluationExecutionResult(
  int Accepted,
  TriggerStatus TriggerStatus,
  string? Reason,
  IReadOnlyList<PrimitiveTapStepOutcome> StepOutcomes);
