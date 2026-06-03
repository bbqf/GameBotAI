namespace GameBot.Service.Contracts.QueueTemplates {
  /// <summary>
  /// A single entry in a <see cref="SaveQueueTemplateRequest"/>. Carries the sequence reference
  /// and optional schedule configuration.
  /// </summary>
  internal sealed class TemplateEntrySaveRequest {
    /// <summary>ID of the sequence to reference. Must be non-blank.</summary>
    public string? SequenceId { get; set; }

    /// <summary>
    /// Schedule type string: "OncePerRun", "EveryStep", or "Timer".
    /// When absent or null, defaults to "OncePerRun".
    /// </summary>
    public string? ScheduleType { get; set; }

    /// <summary>
    /// Required when <see cref="ScheduleType"/> is "Timer". Wall-clock time-of-day in HH:mm
    /// (24-hour, server local time). Ignored for other schedule types.
    /// </summary>
    public string? TimerTimeOfDay { get; set; }
  }
}
