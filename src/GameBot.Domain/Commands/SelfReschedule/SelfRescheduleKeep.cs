namespace GameBot.Domain.Commands.SelfReschedule;

/// <summary>
/// The optional keep rule of a Timer <c>reschedule-self</c> booking (feature 125). Only
/// <see cref="Earliest"/> is available as a payload value.
/// </summary>
public enum SelfRescheduleKeep {
  /// <summary>No keep rule. The last booking of the sequence wins.</summary>
  None = 0,

  /// <summary>Within one run, the booking with the earliest fire time stays pending.</summary>
  Earliest = 1
}
