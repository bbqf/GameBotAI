using System;
using GameBot.Domain.Commands.SelfReschedule;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>Whether a self-reschedule request found an active run to schedule into (feature 065).</summary>
internal enum SelfRescheduleOutcome {
  /// <summary>The run was found and an ephemeral firing was injected into the matching register.</summary>
  Scheduled,

  /// <summary>No active run for the queue (race: the run ended mid-sequence). Treated as a logged no-op.</summary>
  NotRunning
}

/// <summary>
/// Outcome of a self-reschedule request, including the resolved timing for the execution log.
/// </summary>
/// <param name="Outcome">Whether the firing was scheduled or the run was gone.</param>
/// <param name="EntryId">Unique id linking the action's log entry to the resulting firing (FR-014).</param>
/// <param name="Option">The chosen schedule option.</param>
/// <param name="FireAt">Resolved fire instant for Timer options; null otherwise.</param>
/// <param name="ResolvedTiming">Human-readable timing ("this cycle" / "next cycle" / target instant).</param>
/// <param name="KeptPending">True when a <c>keep: earliest</c> booking lost and the pending booking stayed (feature 125).</param>
/// <param name="PendingFireAt">The fire time of the pending booking that stayed. Set only when <paramref name="KeptPending"/> is true.</param>
internal sealed record SelfRescheduleResult(
  SelfRescheduleOutcome Outcome,
  string EntryId,
  SelfRescheduleOption Option,
  DateTimeOffset? FireAt,
  string ResolvedTiming,
  bool KeptPending = false,
  DateTimeOffset? PendingFireAt = null);

/// <summary>Result kinds of a Cancel request (feature 123).</summary>
internal enum SelfRescheduleCancelOutcome {
  /// <summary>At least one pending booking of the sequence was removed.</summary>
  Cancelled,

  /// <summary>The run is active, but the sequence had no pending booking.</summary>
  NothingPending,

  /// <summary>No active run for the queue (race: the run ended mid-sequence).</summary>
  NotRunning
}

/// <summary>Outcome of a Cancel request and the number of bookings that it removed.</summary>
/// <param name="Outcome">What the request found.</param>
/// <param name="RemovedCount">Number of bookings removed (0 unless <see cref="SelfRescheduleCancelOutcome.Cancelled"/>).</param>
internal sealed record SelfRescheduleCancelResult(SelfRescheduleCancelOutcome Outcome, int RemovedCount);

/// <summary>
/// Injects one ephemeral, run-scoped additional firing of a sequence into its originating queue run
/// (feature 065). Depends only on the run registry, the sequence repository, and a
/// <see cref="TimeProvider"/>, so it does not re-form the queue-engine DI cycle.
/// </summary>
internal interface ISelfRescheduleCoordinator {
  /// <summary>
  /// Schedules one additional firing of <paramref name="sequenceId"/> into the active run of
  /// <paramref name="queueId"/> using <paramref name="option"/>. Returns
  /// <see cref="SelfRescheduleOutcome.NotRunning"/> when no active run exists.
  /// </summary>
  /// <param name="queueId">The queue of the active run.</param>
  /// <param name="sequenceId">The sequence to run again.</param>
  /// <param name="option">The booking option.</param>
  /// <param name="timerTimeOfDay">The time of day for a Timer booking.</param>
  /// <param name="timerRelativeOffset">The relative offset for a Timer booking.</param>
  /// <param name="scope">
  /// The parameter scope of the run that makes the booking. The booked run uses this scope. Null
  /// means the queue scope (feature 116).
  /// </param>
  /// <param name="keep">
  /// The keep rule of a Timer booking (feature 125). With <see cref="SelfRescheduleKeep.Earliest"/>, a
  /// booking of the same run does not replace an earlier pending booking.
  /// </param>
  /// <param name="runId">The id of the run that makes the booking (feature 125). Null for an unknown run.</param>
  SelfRescheduleResult ScheduleSelf(
    string queueId,
    string sequenceId,
    SelfRescheduleOption option,
    TimeOnly? timerTimeOfDay,
    TimeSpan? timerRelativeOffset,
    GameBot.Domain.Parameters.ParameterScope? scope = null,
    SelfRescheduleKeep keep = SelfRescheduleKeep.None,
    string? runId = null);

  /// <summary>
  /// Removes the pending one-time bookings of <paramref name="sequenceId"/> from the active run of
  /// <paramref name="queueId"/> (feature 123). It never throws for a missing run or booking.
  /// </summary>
  /// <param name="queueId">The queue of the active run.</param>
  /// <param name="sequenceId">The owner sequence of the bookings.</param>
  SelfRescheduleCancelResult CancelSelf(string queueId, string sequenceId);
}
