using System.Collections.Generic;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// An ordered reference to a sequence within a queue. In-memory only.
  /// A queue may contain the same <see cref="SequenceId"/> more than once; the
  /// <see cref="EntryId"/> distinguishes individual entries for removal.
  /// </summary>
  public class QueueEntry {
    public string EntryId { get; set; } = string.Empty;
    public string SequenceId { get; set; } = string.Empty;
  }

  /// <summary>
  /// In-memory, non-persistent runtime state for a single queue: the ordered entry
  /// list and the current execution status. Reset to empty / <see cref="QueueExecutionStatus.Stopped"/>
  /// whenever the service restarts.
  /// </summary>
  internal class QueueRuntimeState {
    public List<QueueEntry> Entries { get; } = new List<QueueEntry>();
    public QueueExecutionStatus Status { get; set; } = QueueExecutionStatus.Stopped;
  }
}
