using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Services;
using GameBot.Service.Services.ExecutionLog;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>
/// Executes a single authored sequence end-to-end: creates the execution-log root (or a nested
/// child when <paramref name="parentContext"/> is supplied, e.g. a queue run), runs the sequence
/// via <see cref="SequenceRunner"/> with command/gate/condition wiring, and finalizes the entry.
/// Shared by the standalone <c>sequences/{id}/execute</c> endpoint and the queue execution engine.
/// </summary>
internal interface ISequenceExecutionService {
  Task<SequenceExecutionResult> ExecuteAsync(
    string sequenceId,
    string? sessionId,
    ExecutionLogContext? parentContext,
    CancellationToken ct = default);

  /// <summary>
  /// Executes a sequence with a parameter scope (feature 078). The scope supplies the queue built-ins
  /// and the firing entry's values, and is inherited by every command the sequence invokes.
  /// </summary>
  /// <param name="sequenceId">Sequence to run.</param>
  /// <param name="sessionId">Session to run against.</param>
  /// <param name="parentContext">Execution-log context linking this firing to its parent.</param>
  /// <param name="scope">Scope in effect for this firing.</param>
  /// <param name="ct">Cancellation token.</param>
  Task<SequenceExecutionResult> ExecuteAsync(
    string sequenceId,
    string? sessionId,
    ExecutionLogContext? parentContext,
    GameBot.Domain.Parameters.ParameterScope scope,
    CancellationToken ct = default);
}
