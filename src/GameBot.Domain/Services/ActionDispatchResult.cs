namespace GameBot.Domain.Services;

/// <summary>
/// Result of dispatching a non-command sequence action (e.g. <c>reschedule-self</c>) through the
/// optional action dispatcher supplied to <see cref="SequenceRunner.ExecuteAsync"/> (feature 065).
/// The runner records this as the action step's result and continues — the action never, by
/// itself, terminates the sequence (FR-012).
/// </summary>
/// <param name="Outcome">Action outcome recorded on the step (e.g. <c>scheduled</c>, <c>noop</c>).</param>
/// <param name="Message">Optional human-readable detail for the execution log.</param>
/// <param name="Removed">
/// For the <c>Cancel</c> option: true when the action removed a booking, false when it did nothing.
/// Null for all other options.
/// </param>
/// <param name="SmartScheduling">
/// For a <c>Timer</c> booking that smart scheduling postponed (feature 189): the values for the execution log. Null
/// for all other bookings and options.
/// </param>
public sealed record ActionDispatchResult(
  string Outcome, string? Message, bool? Removed = null, SmartSchedulingLogInfo? SmartScheduling = null);

/// <summary>What smart scheduling did with a Timer booking, for the execution log (feature 189).</summary>
/// <param name="RequestedAt">The time that the step requested.</param>
/// <param name="BookedAt">The booked time.</param>
/// <param name="TargetSequenceId">The sequence of the pending firing that the booking moved to.</param>
/// <param name="TargetKind">The kind of that firing: <c>SelfReschedule</c>, <c>TemplateTimer</c>, <c>DailyRetry</c>, or <c>LiveSchedule</c>.</param>
/// <param name="WindowMinutes">The window that the rule used, in minutes.</param>
/// <param name="TargetName">The name of the target sequence. Null when the name is not known: the log then shows the ID.</param>
public sealed record SmartSchedulingLogInfo(
  System.DateTimeOffset RequestedAt,
  System.DateTimeOffset BookedAt,
  string TargetSequenceId,
  string TargetKind,
  int WindowMinutes,
  string? TargetName = null) {
  /// <summary>The text that the execution log shows for the target: the name, or the ID when the name is not known.</summary>
  public string TargetLabel => string.IsNullOrWhiteSpace(TargetName) ? TargetSequenceId : TargetName;
}
