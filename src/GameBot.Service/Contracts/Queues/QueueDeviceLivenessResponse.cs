using System;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// The device liveness of a running queue (feature 106, data-model section 10, contract
  /// <c>queue-device-liveness.md</c>). The read calculates it from the current data.
  /// </summary>
  internal sealed class QueueDeviceLivenessResponse {
    /// <summary><c>live</c>, <c>not_live</c> or <c>unknown</c>.</summary>
    public string State { get; set; } = "unknown";

    /// <summary>The not-live reason. Null when the state is not <c>not_live</c>.</summary>
    public string? Reason { get; set; }

    /// <summary>When the queue first observed the device as not live in this fault episode (service-local time).</summary>
    public DateTimeOffset? NotLiveSince { get; set; }

    /// <summary>The stale flag.</summary>
    public bool Stale { get; set; }

    /// <summary>Milliseconds since the last completed capture.</summary>
    public long? FrameAgeMs { get; set; }

    /// <summary>Milliseconds since the frame bytes last changed.</summary>
    public long? UnchangedMs { get; set; }

    /// <summary>The number of held firings in the current fault episode. 0 when no episode is open.</summary>
    public int GatedFirings { get; set; }
  }
}
