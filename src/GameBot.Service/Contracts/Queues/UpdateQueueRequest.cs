namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request body for updating a queue. The bound emulator is intentionally absent:
  /// the emulator binding is immutable after creation.
  /// </summary>
  internal sealed class UpdateQueueRequest {
    public string? Name { get; set; }
    public bool CycleExecution { get; set; }
  }
}
