using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Builds the read-only <see cref="QueueMonitorSnapshot"/> for a queue: a pure projection over the
/// active <see cref="QueueRunHandle"/>, the linked template, and the current wall-clock. Has no side
/// effects and never mutates the run — safe to call on the monitor's poll interval.
/// </summary>
internal interface IQueueMonitorService {
  /// <summary>
  /// Projects the current live plan for <paramref name="queueId"/>. Returns a running snapshot when a
  /// run is registered, otherwise a not-running envelope with a best-effort last outcome. The caller
  /// is responsible for translating a missing queue into a 404 (this returns a not-running snapshot).
  /// </summary>
  Task<QueueMonitorSnapshot> BuildAsync(string queueId, CancellationToken ct = default);
}
