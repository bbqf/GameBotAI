using System;
using System.Collections.ObjectModel;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Recent completed cycles of a queue's current run (feature 086, issue #180), readable <b>while the
  /// queue is running</b> — which is what makes a repeatedly-failing queue diagnosable without
  /// stopping production.
  /// <para>
  /// A known queue that is not running returns <see cref="Running"/> false with an empty list rather
  /// than an error, mirroring the live-monitor endpoint so a polling client renders a state instead of
  /// handling a failure.
  /// </para>
  /// </summary>
  internal sealed class QueueCyclesResponse {
    public string QueueId { get; set; } = string.Empty;

    /// <summary>
    /// Whether a run is in progress. True only when the queue's status is Running <i>and</i> a run
    /// handle exists, the same condition that decides whether <see cref="QueueDetailResponse.Health"/>
    /// is populated — so the two endpoints can never disagree.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>Completed cycles, newest first; empty when not running.</summary>
    public Collection<QueueCycleResponse> Cycles { get; } = new Collection<QueueCycleResponse>();
  }

  /// <summary>One completed cycle — a single pass of the run over its roster.</summary>
  internal sealed class QueueCycleResponse {
    /// <summary>1-based position within the run; may exceed the number of cycles returned.</summary>
    public int Ordinal { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>"failure" iff at least one entry failed; a cycle that executed nothing is "success".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Per-entry outcomes, in execution order.</summary>
    public Collection<QueueCycleEntryResponse> Entries { get; } = new Collection<QueueCycleEntryResponse>();
  }

  /// <summary>The outcome of one sequence firing inside a cycle.</summary>
  internal sealed class QueueCycleEntryResponse {
    public string SequenceId { get; set; } = string.Empty;

    /// <summary>Resolved when the response is built; null when the sequence no longer exists.</summary>
    public string? SequenceName { get; set; }

    /// <summary>"success" or "failure".</summary>
    public string Status { get; set; } = string.Empty;
  }
}
