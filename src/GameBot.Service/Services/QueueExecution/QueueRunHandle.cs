using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands.SelfReschedule;
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

  // ── Self-reschedule ephemeral registers (feature 065) ────────────────────────────────────────
  // All four are in-memory only and discarded with the handle — on normal completion AND on
  // stop/abort (FR-010, FR-017). Unlike PendingLiveSchedules (most-recent-wins per sequence), these
  // accumulate so multiple self-reschedules of the same sequence each produce an independent firing.

  /// <summary>Whether this run cycles; lets the coordinator pick the AtQueueStart register (FR-009).</summary>
  public bool CycleExecution { get; set; }

  /// <summary>OncePerRun firings (and the non-cycling AtQueueStart fallback) drained within the current cycle.</summary>
  public ConcurrentQueue<SelfRescheduleEntry> PendingOncePerRun { get; } = new();

  /// <summary>AtQueueStart firings (cycling) drained at the top of the next cycle, before once-per-run.</summary>
  public ConcurrentQueue<SelfRescheduleEntry> PendingNextCycleStart { get; } = new();

  /// <summary>EveryStep registrations, keyed by sequence id so re-registration is idempotent (loop-safe, FR-008).</summary>
  public ConcurrentDictionary<string, SelfRescheduleEntry> EveryStepInjections { get; } =
    new(StringComparer.Ordinal);

  private readonly List<SelfRescheduleEntry> _pendingTimerFirings = new();
  private readonly object _timerLock = new();

  /// <summary>Adds a resolved Timer firing (fires once at/after its <see cref="SelfRescheduleEntry.FireAt"/>).</summary>
  public void AddTimerFiring(SelfRescheduleEntry entry) {
    lock (_timerLock) {
      _pendingTimerFirings.Add(entry);
    }
  }

  /// <summary>Removes and returns the Timer firings whose <c>FireAt</c> is at or before <paramref name="now"/>.</summary>
  public IReadOnlyList<SelfRescheduleEntry> DrainDueTimerFirings(DateTimeOffset now) {
    lock (_timerLock) {
      if (_pendingTimerFirings.Count == 0) {
        return Array.Empty<SelfRescheduleEntry>();
      }
      var due = new List<SelfRescheduleEntry>();
      for (var i = _pendingTimerFirings.Count - 1; i >= 0; i--) {
        var entry = _pendingTimerFirings[i];
        if (entry.FireAt is { } fireAt && fireAt <= now) {
          due.Add(entry);
          _pendingTimerFirings.RemoveAt(i);
        }
      }
      return due;
    }
  }

  /// <summary>True while any Timer firing is still pending (keeps a non-cycling run alive to honor it).</summary>
  public bool HasPendingTimerFirings {
    get { lock (_timerLock) { return _pendingTimerFirings.Count > 0; } }
  }
}

/// <summary>
/// One ephemeral, run-scoped self-reschedule firing (feature 065). Never persisted; lives on the
/// <see cref="QueueRunHandle"/> until it fires or the run ends.
/// </summary>
internal sealed record SelfRescheduleEntry(
  string Id,
  string SequenceId,
  SelfRescheduleOption Option,
  DateTimeOffset? FireAt);

/// <summary>Outcome of a queue run, used to build the terminating execution-log entry.</summary>
internal sealed record QueueRunResult(
  QueueStopReason StopReason,
  int SequencesExecuted,
  int SequencesFailed,
  int Cycles,
  string? FailureReason);
