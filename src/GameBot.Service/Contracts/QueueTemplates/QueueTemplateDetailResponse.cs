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
    /// Whether this entry participates in queue runs. Always populated from the stored entry;
    /// entries with no stored value are reported as <c>true</c> (enabled). The detail response
    /// returns all entries, including disabled ones, so the template editor can render and toggle them.
    /// </summary>
    public bool Enabled { get; set; } = true;

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

    /// <summary>Values this entry supplies (feature 078); null when it supplies none.</summary>
    public Collection<GameBot.Service.Models.ParameterBindingDto>? ParameterValues { get; set; }

    /// <summary>
    /// True when this entry carries any parameter value, so the template list can badge it as
    /// overridden without the operator opening it (feature 078, FR-030).
    /// </summary>
    public bool HasParameterOverrides { get; set; }

    /// <summary>
    /// Per-parameter effective value and originating scope for this entry (feature 078, FR-028),
    /// computed against the queue currently linked to this template when exactly one is linked.
    /// Null when the referenced sequence declares nothing and the entry supplies nothing.
    /// </summary>
    public Collection<GameBot.Service.Models.ParameterScopeEntryDto>? EffectiveParameters { get; set; }
  }
}
