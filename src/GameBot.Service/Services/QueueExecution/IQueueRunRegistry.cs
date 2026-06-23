namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Singleton store of the <see cref="QueueRunHandle"/>s for queues that are currently running
/// (feature 065). Extracted from <c>QueueExecutionService._runs</c> so the self-reschedule
/// coordinator can look up an active run without a constructor dependency on the queue engine,
/// which would otherwise form a DI cycle (<c>SequenceExecutionService → QueueExecutionService</c>).
/// Holds no service dependencies; the registry is the single owner of run-handle lifetime.
/// </summary>
internal interface IQueueRunRegistry {
  /// <summary>Registers the handle for a starting run. Returns <c>false</c> if one already exists.</summary>
  bool TryAdd(string queueId, QueueRunHandle handle);

  /// <summary>Looks up the active run handle for a queue.</summary>
  bool TryGet(string queueId, out QueueRunHandle handle);

  /// <summary>Removes (and returns) the handle for a run that is ending.</summary>
  bool Remove(string queueId, out QueueRunHandle handle);

  /// <summary>True iff a run is currently registered for the queue.</summary>
  bool IsRunning(string queueId);
}
