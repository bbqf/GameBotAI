namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>A queue as the managed flow sees it.</summary>
/// <param name="Id">The queue id.</param>
/// <param name="Name">The queue name.</param>
/// <param name="LinkedTemplateId">The id of the linked template, or null.</param>
/// <param name="IsRunning">True when a run is in progress.</param>
public sealed record ManagedQueueInfo(string Id, string Name, string? LinkedTemplateId, bool IsRunning);

/// <summary>
/// The port that hides the queue engine from the managed flow (feature 148, research R1). The Service project
/// implements it. A test uses a fake.
/// </summary>
public interface IManagedQueueControl {
  /// <summary>Lists all queues with their state.</summary>
  /// <param name="ct">The cancellation token.</param>
  Task<IReadOnlyList<ManagedQueueInfo>> ListAsync(CancellationToken ct = default);

  /// <summary>Stops a queue and waits for it to settle.</summary>
  /// <param name="queueId">The queue id.</param>
  /// <param name="limit">The longest wait.</param>
  /// <param name="ct">The cancellation token.</param>
  /// <returns>True when the queue stopped in time. False for a timeout.</returns>
  Task<bool> StopAsync(string queueId, TimeSpan limit, CancellationToken ct = default);

  /// <summary>Starts a queue.</summary>
  /// <param name="queueId">The queue id.</param>
  /// <param name="ct">The cancellation token.</param>
  /// <returns>Null when the queue runs. Else the error text.</returns>
  Task<string?> StartAsync(string queueId, CancellationToken ct = default);
}
