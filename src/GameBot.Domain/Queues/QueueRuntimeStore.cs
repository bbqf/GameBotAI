using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// Thread-safe, in-memory implementation of <see cref="IQueueRuntimeStore"/>.
  /// Registered as a singleton; holds no persistence, so all entries and statuses are
  /// lost on restart by design (FR-021, FR-022).
  /// </summary>
  public class QueueRuntimeStore : IQueueRuntimeStore {
    private readonly ConcurrentDictionary<string, QueueRuntimeState> _states =
      new ConcurrentDictionary<string, QueueRuntimeState>(StringComparer.Ordinal);

    private QueueRuntimeState StateFor(string queueId) =>
      _states.GetOrAdd(queueId, _ => new QueueRuntimeState());

    public IReadOnlyList<QueueEntry> GetEntries(string queueId) {
      if (!_states.TryGetValue(queueId, out var state)) return Array.Empty<QueueEntry>();
      lock (state) {
        return state.Entries.ToArray();
      }
    }

    public QueueEntry AddEntry(string queueId, string sequenceId) {
      var entry = new QueueEntry {
        EntryId = Guid.NewGuid().ToString("N"),
        SequenceId = sequenceId
      };
      var state = StateFor(queueId);
      lock (state) {
        state.Entries.Add(entry);
      }
      return entry;
    }

    public IReadOnlyList<QueueEntry> SetEntries(string queueId, IEnumerable<string> sequenceIds) {
      ArgumentNullException.ThrowIfNull(sequenceIds);
      var state = StateFor(queueId);
      lock (state) {
        state.Entries.Clear();
        foreach (var sequenceId in sequenceIds) {
          state.Entries.Add(new QueueEntry {
            EntryId = Guid.NewGuid().ToString("N"),
            SequenceId = sequenceId
          });
        }
        return state.Entries.ToArray();
      }
    }

    public bool RemoveEntry(string queueId, string entryId) {
      if (!_states.TryGetValue(queueId, out var state)) return false;
      lock (state) {
        var index = state.Entries.FindIndex(e => string.Equals(e.EntryId, entryId, StringComparison.Ordinal));
        if (index < 0) return false;
        state.Entries.RemoveAt(index);
        return true;
      }
    }

    public QueueExecutionStatus GetStatus(string queueId) {
      if (!_states.TryGetValue(queueId, out var state)) return QueueExecutionStatus.Stopped;
      lock (state) {
        return state.Status;
      }
    }

    public void SetStatus(string queueId, QueueExecutionStatus status) {
      var state = StateFor(queueId);
      lock (state) {
        state.Status = status;
      }
    }

    public void Remove(string queueId) {
      _states.TryRemove(queueId, out _);
    }
  }
}
