using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Domain.Queues;

/// <summary>
/// Keeps the run statistics of each sequence that a queue runs (feature 105). The values stay after a
/// queue restart and after a service restart. All return values are copies.
/// </summary>
public interface ISequenceRunStatisticsStore {
  /// <summary>
  /// Applies <paramref name="record"/> to the entry of the (queue, sequence) pair and writes the queue
  /// file. Creates the entry and the file when they do not exist.
  /// </summary>
  Task RecordAsync(string queueId, string sequenceId, SequenceRunRecord record, CancellationToken ct = default);

  /// <summary>Returns a copy of all entries of the queue, keyed by sequence ID. Empty when there is no file.</summary>
  Task<IReadOnlyDictionary<string, SequenceRunStatistics>> GetForQueueAsync(string queueId, CancellationToken ct = default);

  /// <summary>Returns a copy of one entry, or null when the pair has no record.</summary>
  Task<SequenceRunStatistics?> GetAsync(string queueId, string sequenceId, CancellationToken ct = default);

  /// <summary>Deletes the queue file and the in-memory copy. Gives no error when there is no file.</summary>
  Task DeleteQueueAsync(string queueId, CancellationToken ct = default);
}
