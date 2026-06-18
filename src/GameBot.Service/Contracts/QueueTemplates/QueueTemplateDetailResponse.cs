using System.Collections.ObjectModel;

namespace GameBot.Service.Contracts.QueueTemplates {
  /// <summary>Single-template representation including its ordered, resolved sequence entries.</summary>
  internal sealed class QueueTemplateDetailResponse : QueueTemplateSummaryResponse {
    public Collection<QueueTemplateEntryResponse> Entries { get; } = new Collection<QueueTemplateEntryResponse>();
  }

  /// <summary>
  /// A template entry projected for responses. <see cref="SequenceName"/> is resolved from the
  /// sequence store; <see cref="Stale"/> is true when the referenced sequence no longer exists.
  /// </summary>
  internal sealed class QueueTemplateEntryResponse {
    public string SequenceId { get; set; } = string.Empty;
    public string? SequenceName { get; set; }
    public bool Stale { get; set; }

    /// <summary>
    /// Schedule type of this entry: "OncePerRun", "EveryStep", "Timer", or "AtQueueStart".
    /// ("EveryStep" is displayed to operators as "After Every Step"; the returned value is unchanged.)
    /// </summary>
    public string ScheduleType { get; set; } = "OncePerRun";

    /// <summary>
    /// Wall-clock time-of-day in HH:mm (24-hour) when <see cref="ScheduleType"/> is "Timer" in
    /// time-of-day mode; null otherwise.
    /// </summary>
    public string? TimerTimeOfDay { get; set; }

    /// <summary>
    /// Relative offset in HH:mm:ss when <see cref="ScheduleType"/> is "Timer" in relative mode;
    /// null otherwise.
    /// </summary>
    public string? TimerRelativeOffset { get; set; }
  }
}
