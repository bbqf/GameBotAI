namespace GameBot.Service.Contracts.QueueTemplates {
  /// <summary>
  /// A single entry in a <see cref="SaveQueueTemplateRequest"/>. Carries the sequence reference
  /// and optional schedule configuration.
  /// </summary>
  internal sealed class TemplateEntrySaveRequest {
    /// <summary>ID of the sequence to reference. Must be non-blank.</summary>
    public string? SequenceId { get; set; }

    /// <summary>
    /// Whether the entry participates in queue runs. When null (older clients that omit it) the
    /// entry is treated as enabled. When <c>false</c>, the entry is retained in the template but
    /// excluded from runs.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Schedule type string: "OncePerRun", "EveryStep", "Timer", or "AtQueueStart".
    /// When absent or null, defaults to "OncePerRun". ("EveryStep" is displayed to operators as
    /// "After Every Step"; the wire value is unchanged.)
    /// </summary>
    public string? ScheduleType { get; set; }

    /// <summary>
    /// Time-of-day mode for a "Timer" entry: wall-clock time-of-day in HH:mm (24-hour, server
    /// local time). Mutually exclusive with <see cref="TimerRelativeOffset"/>; ignored for other
    /// schedule types.
    /// </summary>
    public string? TimerTimeOfDay { get; set; }

    /// <summary>
    /// Relative mode for a "Timer" entry: duration offset from the queue run start, as an
    /// "HH:mm:ss" string (e.g. "00:10:00"). Must be non-negative and at most 24:00:00. Mutually
    /// exclusive with <see cref="TimerTimeOfDay"/>; ignored for other schedule types.
    /// </summary>
    public string? TimerRelativeOffset { get; set; }

    /// <summary>
    /// Values this entry supplies to its sequence's run scope (feature 078). Holds both declared
    /// bindings and ad-hoc names; ad-hoc values reach any command beneath the entry at any depth, so
    /// an intermediate sequence never has to re-declare a pass-through value. A name that nothing
    /// consumes is reported as a warning, never an error.
    /// </summary>
    public GameBot.Service.Models.ParameterBindingDto[]? ParameterValues { get; set; }
  }
}
