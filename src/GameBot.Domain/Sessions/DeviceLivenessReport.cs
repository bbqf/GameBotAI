namespace GameBot.Domain.Sessions;

/// <summary>
/// The result of <see cref="DeviceLivenessEvaluator.Evaluate"/> (feature 106, data-model section 4).
/// </summary>
/// <param name="State"><c>live</c>, <c>not_live</c> or <c>unknown</c> (<see cref="DeviceLivenessStates"/>).</param>
/// <param name="Reason">The not-live reason (<see cref="DeviceLivenessReasons"/>). Null when the state is not <c>not_live</c>.</param>
/// <param name="FrameAgeMs">Milliseconds since the last completed capture. Null when no capture completed.</param>
/// <param name="UnchangedMs">Milliseconds since the frame bytes last changed. Null when no capture completed.</param>
/// <param name="Stale">True when a capture loop runs and the frame is old or did not change for a long time.</param>
/// <param name="LastInputAt">The start of the last input command.</param>
/// <param name="LastInputOutcome">The API text of the last input outcome. Null when no input was sent.</param>
/// <param name="NeedsProbe">True when rule 7 gave the state. The health call then does one direct capture. Not in the API.</param>
public sealed record DeviceLivenessReport(
  string State,
  string? Reason,
  long? FrameAgeMs,
  long? UnchangedMs,
  bool Stale,
  DateTimeOffset? LastInputAt,
  string? LastInputOutcome,
  bool NeedsProbe) {
  /// <summary>True when the state is <c>not_live</c> with a reason in <see cref="DeviceLivenessReasons.Hard"/>.</summary>
  public bool IsHardNotLive =>
    State == DeviceLivenessStates.NotLive && Reason is not null && DeviceLivenessReasons.Hard.Contains(Reason);
}
