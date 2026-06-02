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
}
