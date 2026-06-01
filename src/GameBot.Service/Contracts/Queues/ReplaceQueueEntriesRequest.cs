namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request body for replacing all of a queue's runtime entries with an ordered list of
  /// sequence ids (used to load a template into a queue). An empty array clears the entries.
  /// </summary>
  internal sealed class ReplaceQueueEntriesRequest {
    public string[]? SequenceIds { get; set; }
  }
}
