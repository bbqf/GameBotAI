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

  /// <summary>
  /// Force-executes a command with a parameter scope and returns its per-step outcomes, so a caller
  /// can tell whether the command actually put input on the device rather than only that it did not
  /// throw. The sequence runner needs this to avoid recording <c>executed</c> for a command whose
  /// image-anchored tap never found its template.
  /// </summary>
  Task<CommandForceExecutionResult> ForceExecuteDetailedAsync(string? sessionId, string commandId, ExecutionLogContext context, ParameterScope scope, CancellationToken ct = default);
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

  /// <summary>
  /// Feature 112 (issue #222): executes one step with a time limit and writes one execution-log entry of the
  /// type <c>step</c> for the call. The entry is written for each call that passes the session check: for an
  /// outcome, a timeout, a cancellation and an error. A failure of the log write does not change the result.
  /// </summary>
  /// <param name="sessionId">The session to use, or null to use the only running session.</param>
  /// <param name="step">The step to execute.</param>
  /// <param name="timeout">The time limit of the step, or null for no limit.</param>
  /// <param name="ct">The cancellation token of the caller.</param>
  /// <exception cref="TimeoutException">The time limit stopped the step. The message is <c>step_execution_timeout</c>.</exception>
  Task<CommandForceExecutionResult> ForceExecuteStepAsync(string? sessionId, GameBot.Domain.Commands.CommandStep step, TimeSpan? timeout, CancellationToken ct = default);
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
  IReadOnlyList<ResolvedParameter>? ResolvedParameters = null,
  int? HoldMs = null);

internal sealed record CommandForceExecutionResult(int Accepted, IReadOnlyList<PrimitiveTapStepOutcome> StepOutcomes);

internal sealed record CommandEvaluationExecutionResult(
  int Accepted,
  TriggerStatus TriggerStatus,
  string? Reason,
  IReadOnlyList<PrimitiveTapStepOutcome> StepOutcomes);
