using System.Collections.Generic;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// In-memory, non-persistent store for the runtime parts of a queue: its ordered
  /// sequence entries and its execution status. A process restart discards everything
  /// here, which is exactly the intended behavior (entries empty, status Stopped).
  /// </summary>
  public interface IQueueRuntimeStore {
    /// <summary>Ordered entries for the queue; empty for an unknown queue.</summary>
    IReadOnlyList<QueueEntry> GetEntries(string queueId);

    /// <summary>
    /// True iff a runtime state record currently exists for the queue. Distinguishes a queue
    /// that has never been materialized this service lifetime (false) from one that exists but
    /// may be empty (true, e.g. the operator cleared its entries). Backs the auto-load
    /// "first display only" guard so a deliberately emptied queue is not re-filled.
    /// </summary>
    bool HasRuntimeState(string queueId);

    /// <summary>Appends a new entry referencing <paramref name="sequenceId"/> at the end.</summary>
    QueueEntry AddEntry(string queueId, string sequenceId);

    /// <summary>Removes an entry by id; returns false if not found.</summary>
    bool RemoveEntry(string queueId, string entryId);

    /// <summary>
    /// Replaces all entries for the queue with fresh entries referencing the given sequence ids,
    /// preserving the given order and assigning a new EntryId to each. An empty input clears the
    /// queue's entries. Used to load a template into a queue.
    /// </summary>
    IReadOnlyList<QueueEntry> SetEntries(string queueId, IEnumerable<string> sequenceIds);

    /// <summary>Current status; <see cref="QueueExecutionStatus.Stopped"/> for an unknown queue.</summary>
    QueueExecutionStatus GetStatus(string queueId);

    /// <summary>Sets the execution status (idempotent at the caller's discretion).</summary>
    void SetStatus(string queueId, QueueExecutionStatus status);

    /// <summary>Discards all runtime state for a queue (used on queue delete).</summary>
    void Remove(string queueId);
  }
}
