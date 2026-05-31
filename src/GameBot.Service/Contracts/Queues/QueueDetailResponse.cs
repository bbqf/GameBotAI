using System.Collections.ObjectModel;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>Single-queue representation including its ordered sequence entries.</summary>
  internal sealed class QueueDetailResponse : QueueResponse {
    public Collection<QueueEntryResponse> Entries { get; } = new Collection<QueueEntryResponse>();
  }

  /// <summary>
  /// A queue entry projected for responses. <see cref="SequenceName"/> is resolved from the
  /// sequence store; <see cref="Stale"/> is true when the referenced sequence no longer exists.
  /// </summary>
  internal sealed class QueueEntryResponse {
    public string EntryId { get; set; } = string.Empty;
    public string SequenceId { get; set; } = string.Empty;
    public string? SequenceName { get; set; }
    public bool Stale { get; set; }
  }
}
