namespace GameBot.Domain.Sessions;

/// <summary>
/// Keeps the liveness data of each session (feature 106, data-model sections 2 and 5): the capture
/// loop state, the time of the last capture and the last frame change, and the last input. The
/// capture loop and the session manager write to it. The endpoints and the queue read from it.
/// </summary>
public interface IDeviceLivenessTracker {
  /// <summary>The current time of the tracker clock.</summary>
  DateTimeOffset Now { get; }

  /// <summary>A capture loop started for the session. Creates the record and clears the earlier data.</summary>
  void LoopStarted(string sessionId);

  /// <summary>The capture loop of the session stopped. Keeps the data.</summary>
  void LoopStopped(string sessionId);

  /// <summary>A capture completed. Does nothing for an unknown session or a stopped loop.</summary>
  void RecordCapture(string sessionId, bool changed);

  /// <summary>An input command starts. Creates the record when it does not exist.</summary>
  void RecordInputStarted(string sessionId);

  /// <summary>An input command ended with <paramref name="outcome"/>. Does nothing for an unknown session.</summary>
  void RecordInputCompleted(string sessionId, InputOutcome outcome);

  /// <summary>Deletes the record of the session.</summary>
  void Remove(string sessionId);

  /// <summary>Returns a copy of the data of the session. An unknown session gives an empty sample.</summary>
  DeviceLivenessSample Sample(string sessionId, bool hasDevice, bool? transportReady = null);

  /// <summary>True when a capture loop runs, or ran, for the session.</summary>
  bool HasCaptureData(string sessionId);
}
