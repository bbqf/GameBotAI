namespace GameBot.Service.Contracts.Queues {
  /// <summary>Request body for appending a sequence entry to a queue.</summary>
  internal sealed class AddQueueEntryRequest {
    public string? SequenceId { get; set; }
  }
}
