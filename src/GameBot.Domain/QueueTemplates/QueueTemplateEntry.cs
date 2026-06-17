using System;

namespace GameBot.Domain.QueueTemplates {
  /// <summary>
  /// A single, positional reference to a sequence within a <see cref="QueueTemplate"/>.
  /// Only the sequence reference and schedule configuration are persisted; the resolved display
  /// name and stale flag are computed at read time. The same SequenceId may appear more than once
  /// in a template.
  /// </summary>
  public class QueueTemplateEntry {
    /// <summary>ID of the referenced sequence.</summary>
    public string SequenceId { get; set; } = string.Empty;

    /// <summary>
    /// Controls when this entry's sequence is executed during a queue run.
    /// Defaults to <see cref="ScheduleType.OncePerRun"/>, preserving pre-feature behaviour for
    /// entries that were persisted before schedule types were introduced.
    /// </summary>
    public ScheduleType ScheduleType { get; set; } = ScheduleType.OncePerRun;

    /// <summary>
    /// Wall-clock time-of-day (server local time) at which this entry fires when
    /// <see cref="ScheduleType"/> is <see cref="ScheduleType.Timer"/> in <b>time-of-day mode</b>.
    /// Null in relative mode and for all non-timer types.
    /// The sequence executes at most once per calendar day: it fires at the first iteration
    /// boundary after this time has passed today, provided it has not already fired today in the
    /// current run.
    /// </summary>
    public TimeOnly? TimerTimeOfDay { get; set; }

    /// <summary>
    /// Relative duration offset (measured from the queue run start) at which this entry fires when
    /// <see cref="ScheduleType"/> is <see cref="ScheduleType.Timer"/> in <b>relative mode</b>.
    /// Null in time-of-day mode and for all non-timer types.
    /// <para>
    /// The timer "mode" is inferred from which field is set: a <see cref="ScheduleType.Timer"/>
    /// entry MUST have exactly one of <see cref="TimerTimeOfDay"/> / <see cref="TimerRelativeOffset"/>
    /// non-null (enforced at the API layer). In relative mode the sequence fires once per run, at the
    /// first iteration boundary at or after this much time has elapsed since the run started, and is
    /// recomputed fresh on every run. An offset of <c>00:00:00</c> fires at the first iteration
    /// boundary. Serializes as an "HH:mm:ss" string.
    /// </para>
    /// </summary>
    public TimeSpan? TimerRelativeOffset { get; set; }
  }
}
