namespace GameBot.Service.Contracts.Queues {
  /// <summary>Request body for creating a queue.</summary>
  internal sealed class CreateQueueRequest {
    public string? Name { get; set; }
    public string? EmulatorSerial { get; set; }
    public bool CycleExecution { get; set; }
  }
}
