namespace GameBot.Service.Contracts.Queues {
  /// <summary>Request body for creating a queue.</summary>
  internal sealed class CreateQueueRequest {
    public string? Name { get; set; }
    public string? EmulatorSerial { get; set; }
    public bool CycleExecution { get; set; }

    /// <summary>Opt-in idle-pause (feature 073). Absent → false.</summary>
    public bool PauseWhenIdle { get; set; }

    /// <summary>Idle-detection threshold in seconds. Absent or &lt; 1 → coerced to the default 30.</summary>
    public int IdleThresholdSeconds { get; set; }

    /// <summary>Optional LDPlayer instance name to cold-start before session creation (feature 074). Absent → no emulator management.</summary>
    public string? EmulatorInstanceName { get; set; }

    /// <summary>Optional LDPlayer instance index for the pre-session cold-start (feature 074). When supplied MUST be ≥ 0.</summary>
    public int? EmulatorInstanceIndex { get; set; }

    /// <summary>Optional failure policy (feature 087). Absent or null → no policy, no evaluation.</summary>
    public QueueFailurePolicyDto? FailurePolicy { get; set; }

    /// <summary>Opt-in: start the queue again after a service restart if it was running (feature 098). Absent → false.</summary>
    public bool ResumeOnServiceStart { get; set; }
  }
}
