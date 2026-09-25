using System.Collections.Generic;

namespace GameBot.Domain.Sessions;

/// <summary>The states of the device liveness report (feature 106).</summary>
public static class DeviceLivenessStates {
  /// <summary>The device answers and its frames are current.</summary>
  public const string Live = "live";

  /// <summary>The device does not answer, or it does not apply the inputs.</summary>
  public const string NotLive = "not_live";

  /// <summary>The service has not sufficient data to decide.</summary>
  public const string Unknown = "unknown";
}

/// <summary>The reasons of a <see cref="DeviceLivenessStates.NotLive"/> report (feature 106).</summary>
public static class DeviceLivenessReasons {
  /// <summary>No capture completed for longer than the capture-stall limit.</summary>
  public const string CaptureStalled = "capture_stalled";

  /// <summary>An input command did not complete in the input time limit.</summary>
  public const string InputTimeout = "input_timeout";

  /// <summary>The frame did not change for the stale limit after an input.</summary>
  public const string NoChangeAfterInput = "no_change_after_input";

  /// <summary>The ADB transport of the device is not ready.</summary>
  public const string TransportNotReady = "transport_not_ready";

  /// <summary>All reasons, in the order of the evaluation rules.</summary>
  public static readonly IReadOnlyList<string> All = new[] { TransportNotReady, InputTimeout, CaptureStalled, NoChangeAfterInput };

  /// <summary>
  /// The hard reasons. They are direct signs of a fault. Only these reasons hold a queue firing
  /// (research R-011). <see cref="NoChangeAfterInput"/> is not in this set.
  /// </summary>
  public static readonly IReadOnlySet<string> Hard = new HashSet<string>(StringComparer.Ordinal) {
    CaptureStalled,
    InputTimeout,
    TransportNotReady
  };
}

/// <summary>The outcome of the last input command of a session (feature 106).</summary>
public enum InputOutcome {
  /// <summary>The input started and did not end yet.</summary>
  Pending,

  /// <summary>The device accepted the input.</summary>
  Completed,

  /// <summary>The device did not answer in the input time limit.</summary>
  TimedOut,

  /// <summary>The device refused the input on all attempts.</summary>
  Failed,

  /// <summary>The caller stopped the input before the input time limit.</summary>
  Cancelled
}

/// <summary>Converts <see cref="InputOutcome"/> values to their API text.</summary>
public static class InputOutcomes {
  /// <summary>All API values, in enum order.</summary>
  public static readonly IReadOnlyList<string> WireValues = new[] { "pending", "completed", "timed_out", "failed", "cancelled" };

  /// <summary>Returns the API text of <paramref name="outcome"/>.</summary>
  public static string ToWire(InputOutcome outcome) => outcome switch {
    InputOutcome.Pending => "pending",
    InputOutcome.Completed => "completed",
    InputOutcome.TimedOut => "timed_out",
    InputOutcome.Failed => "failed",
    _ => "cancelled"
  };
}
