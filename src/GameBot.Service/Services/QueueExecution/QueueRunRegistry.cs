using System;
using System.Collections.Concurrent;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Default <see cref="IQueueRunRegistry"/>: a singleton owning a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> of active <see cref="QueueRunHandle"/>s keyed by
/// queue id. Relocated from <c>QueueExecutionService</c>; adds no behavior of its own.
/// </summary>
internal sealed class QueueRunRegistry : IQueueRunRegistry {
  private readonly ConcurrentDictionary<string, QueueRunHandle> _runs =
    new(StringComparer.Ordinal);

  public bool TryAdd(string queueId, QueueRunHandle handle) => _runs.TryAdd(queueId, handle);

  public bool TryGet(string queueId, out QueueRunHandle handle) {
    if (_runs.TryGetValue(queueId, out var found)) {
      handle = found;
      return true;
    }
    handle = null!;
    return false;
  }

  public bool Remove(string queueId, out QueueRunHandle handle) {
    if (_runs.TryRemove(queueId, out var removed)) {
      handle = removed;
      return true;
    }
    handle = null!;
    return false;
  }

  public bool IsRunning(string queueId) => _runs.ContainsKey(queueId);
}
