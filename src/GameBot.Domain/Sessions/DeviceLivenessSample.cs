namespace GameBot.Domain.Sessions;

/// <summary>
/// A copy of the liveness data of one session, plus two values from the caller (feature 106,
/// data-model section 3). <see cref="DeviceLivenessEvaluator"/> reads it.
/// </summary>
/// <param name="HasDevice">The session has a device serial. False in stub mode.</param>
/// <param name="TransportReady">The result of the transport check. Null when the caller did not check.</param>
/// <param name="CaptureLoopRunning">A capture loop runs for the session.</param>
/// <param name="LoopStartedAt">The start of the current capture loop.</param>
/// <param name="LastCaptureAt">The time of the last completed capture.</param>
/// <param name="LastChangeAt">The time of the last capture whose bytes were different.</param>
/// <param name="FirstInputAfterChangeAt">The start of the first input after the last frame change.</param>
/// <param name="LastInputAt">The start of the last input command.</param>
/// <param name="LastInputOutcome">The outcome of the last input command.</param>
public sealed record DeviceLivenessSample(
  bool HasDevice,
  bool? TransportReady,
  bool CaptureLoopRunning,
  DateTimeOffset? LoopStartedAt,
  DateTimeOffset? LastCaptureAt,
  DateTimeOffset? LastChangeAt,
  DateTimeOffset? FirstInputAfterChangeAt,
  DateTimeOffset? LastInputAt,
  InputOutcome? LastInputOutcome);
