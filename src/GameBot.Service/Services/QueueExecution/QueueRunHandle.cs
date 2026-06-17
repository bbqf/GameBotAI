using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Queues;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// In-memory record of one currently-running queue. Not persisted; discarded when the run ends.
/// </summary>
internal sealed class QueueRunHandle {
  public required string QueueId { get; init; }

  /// <summary>Cancelled by <c>StopAsync</c> (and on host shutdown) to abort the run.</summary>
  public required CancellationTokenSource Cts { get; init; }

  /// <summary>The background orchestration task; assigned right after launch.</summary>
  public Task RunTask { get; set; } = Task.CompletedTask;

  /// <summary>The queue-run execution-log root id for this run.</summary>
  public string? RootExecutionId { get; set; }

  /// <summary>The emulator session opened for this run; null until connected.</summary>
  public string? SessionId { get; set; }

  public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

  /// <summary>
  /// The local-clock instant captured once when the run loop starts; the anchor against which
  /// template relative-offset timers are measured (feature 059). Set by the run loop.
  /// </summary>
  public DateTimeOffset RunStartedAt { get; set; }

  /// <summary>
  /// Live, ephemeral relative schedules requested against this run via the live-schedule endpoint.
  /// Key = sequence id; value = expected fire instant (call time + offset, local clock). Upserts are
  /// most-recent-wins per sequence (FR-011); an entry is removed once fired (fires once, FR-009).
  /// Never persisted — discarded with the handle when the run ends (FR-008).
  /// </summary>
  public ConcurrentDictionary<string, DateTimeOffset> PendingLiveSchedules { get; } =
    new(StringComparer.Ordinal);
}

/// <summary>Outcome of a queue run, used to build the terminating execution-log entry.</summary>
internal sealed record QueueRunResult(
  QueueStopReason StopReason,
  int SequencesExecuted,
  int SequencesFailed,
  int Cycles,
  string? FailureReason);
