namespace GameBot.Service.Contracts.Queues {
  /// <summary>Request body for duplicating a queue. The new name MUST differ from the source queue's current name.</summary>
  internal sealed class DuplicateQueueRequest {
    public string? Name { get; set; }
  }
}
