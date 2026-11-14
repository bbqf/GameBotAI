using System;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Queues;

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
/// <param name="RequestedAt">
/// The requested time of a Timer booking that smart scheduling postponed (feature 189). <paramref name="FireAt"/> is
/// then the booked time. Null when the booking is not postponed.
/// </param>
/// <param name="SmartTarget">The pending firing that the booking moved to (feature 189). Null when the booking is not postponed.</param>
internal sealed record SelfRescheduleResult(
  SelfRescheduleOutcome Outcome,
  string EntryId,
  SelfRescheduleOption Option,
  DateTimeOffset? FireAt,
  string ResolvedTiming,
  bool KeptPending = false,
  DateTimeOffset? PendingFireAt = null,
  DateTimeOffset? RequestedAt = null,
  SmartCandidate? SmartTarget = null);

/// <summary>
/// Asks the coordinator to apply smart scheduling to a Timer booking (feature 189). The caller gives it only when the
/// stored queue has smart scheduling on and the sequence does not ignore it.
/// </summary>
/// <param name="WindowMinutes">The window in minutes, from 1 to 120.</param>
internal sealed record SmartSchedulingRequest(int WindowMinutes);

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
  /// <param name="smartScheduling">
  /// The smart scheduling request of a Timer booking (feature 189). When not null, the booking moves to the earliest
  /// pending firing of a different sequence that is later than the requested time and not more than the window later.
  /// Null books the requested time. The other options ignore it.
  /// </param>
  SelfRescheduleResult ScheduleSelf(
    string queueId,
    string sequenceId,
    SelfRescheduleOption option,
    TimeOnly? timerTimeOfDay,
    TimeSpan? timerRelativeOffset,
    GameBot.Domain.Parameters.ParameterScope? scope = null,
    SelfRescheduleKeep keep = SelfRescheduleKeep.None,
    string? runId = null,
    SmartSchedulingRequest? smartScheduling = null);

  /// <summary>
  /// Removes the pending one-time bookings of <paramref name="sequenceId"/> from the active run of
  /// <paramref name="queueId"/> (feature 123). It never throws for a missing run or booking.
  /// </summary>
  /// <param name="queueId">The queue of the active run.</param>
  /// <param name="sequenceId">The owner sequence of the bookings.</param>
  SelfRescheduleCancelResult CancelSelf(string queueId, string sequenceId);

  /// <summary>
  /// Resolves the fire time of a Timer booking without a write (feature 127). A relative offset gives now plus
  /// the offset. A time of day gives its next occurrence on the local clock. A step-through uses this to show
  /// <c>would reschedule at 14:30</c> and does not book anything.
  /// </summary>
  /// <param name="timerTimeOfDay">The time of day for a Timer booking.</param>
  /// <param name="timerRelativeOffset">The relative offset for a Timer booking.</param>
  DateTimeOffset ResolveTimerFireAt(TimeOnly? timerTimeOfDay, TimeSpan? timerRelativeOffset);
}
