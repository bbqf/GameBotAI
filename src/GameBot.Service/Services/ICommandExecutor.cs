using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Service.Services;

internal interface ICommandExecutor {
  Task<int> ForceExecuteAsync(string sessionId, string commandId, CancellationToken ct = default);
  Task<int> EvaluateAndExecuteAsync(string sessionId, string commandId, CancellationToken ct = default);
}
