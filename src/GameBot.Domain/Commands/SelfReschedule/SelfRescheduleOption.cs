namespace GameBot.Domain.Commands.SelfReschedule;

/// <summary>
/// The schedule option chosen for a self-reschedule action (feature 065). Mirrors the
/// queue-template <c>ScheduleType</c> vocabulary so authors see no new concepts; named distinctly
/// because the <see cref="AtQueueStart"/> mid-run meaning is option-specific (FR-009).
/// </summary>
public enum SelfRescheduleOption {
  /// <summary>Fire at the next cycle start (cycling run) / next iteration boundary (non-cycling). FR-009.</summary>
  AtQueueStart,

  /// <summary>Append after the remaining once-per-run steps of the current cycle. FR-007.</summary>
  OncePerRun,

  /// <summary>Fire once at a resolved target instant (relative offset or time-of-day). FR-005/FR-006.</summary>
  Timer,

  /// <summary>Register to fire after each subsequent normal step (loop-safe). FR-008.</summary>
  EveryStep
}
