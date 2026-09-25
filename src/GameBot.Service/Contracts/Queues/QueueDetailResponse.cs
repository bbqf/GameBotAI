using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>Single-queue representation including its ordered sequence entries.</summary>
  internal sealed class QueueDetailResponse : QueueResponse {
    public Collection<QueueEntryResponse> Entries { get; } = new Collection<QueueEntryResponse>();

    /// <summary>
    /// Display name of the linked queue template, resolved from the template store; null when
    /// the queue is unlinked or the linked template can no longer be resolved.
    /// </summary>
    public string? LinkedTemplateName { get; set; }

    /// <summary>
    /// Live health of the current run (feature 086); null when the queue is not running. Additive —
    /// every other field keeps its existing name and meaning.
    /// </summary>
    public QueueHealthResponse? Health { get; set; }

    /// <summary>
    /// The run statistics of each sequence that the queue ran (feature 105), keyed by sequence ID in
    /// ordinal order. Never null: an empty object when the queue has no recorded run.
    /// </summary>
    public SortedDictionary<string, QueueSequenceStatsResponse> SequenceStats { get; } = new(StringComparer.Ordinal);
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
