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
internal sealed record SelfRescheduleResult(
  SelfRescheduleOutcome Outcome,
  string EntryId,
  SelfRescheduleOption Option,
  DateTimeOffset? FireAt,
  string ResolvedTiming);

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
  SelfRescheduleResult ScheduleSelf(
    string queueId,
    string sequenceId,
    SelfRescheduleOption option,
    TimeOnly? timerTimeOfDay,
    TimeSpan? timerRelativeOffset);
}
