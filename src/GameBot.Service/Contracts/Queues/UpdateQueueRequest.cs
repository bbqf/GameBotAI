namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request body for updating a queue. The bound emulator is intentionally absent:
  /// the emulator binding is immutable after creation.
  /// </summary>
  internal sealed class UpdateQueueRequest {
    public string? Name { get; set; }
    public bool CycleExecution { get; set; }

    /// <summary>Opt-in idle-pause (feature 073). Absent → false.</summary>
    public bool PauseWhenIdle { get; set; }

    /// <summary>Idle-detection threshold in seconds. Absent or &lt; 1 → coerced to the default 30.</summary>
    public int IdleThresholdSeconds { get; set; }
  }
}
