using System;
using System.Collections.ObjectModel;
using GameBot.Domain.Parameters;

namespace GameBot.Domain.QueueTemplates {
  /// <summary>
  /// A single, positional reference to a sequence within a <see cref="QueueTemplate"/>.
  /// Only the sequence reference and schedule configuration are persisted; the resolved display
  /// name and stale flag are computed at read time. The same SequenceId may appear more than once
  /// in a template.
  /// </summary>
  public class QueueTemplateEntry {
    private readonly Collection<ParameterBinding> _parameterValues = new();

    /// <summary>ID of the referenced sequence.</summary>
    public string SequenceId { get; set; } = string.Empty;

    /// <summary>
    /// Whether this entry participates in queue runs. When <c>false</c>, the entry is retained in
    /// the template (position, schedule, and sequence reference preserved) but is excluded when a
    /// run is built from the template, so it never fires and never affects scheduling.
    /// Defaults to <c>true</c>; templates persisted before this field existed omit it in JSON and
    /// therefore deserialize as enabled, preserving pre-feature behaviour. Independent per entry,
    /// including duplicate references to the same sequence.
    /// </summary>
    public bool Enabled { get; set; } = true;

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

    /// <summary>
    /// Values this entry supplies to its sequence's run scope (feature 078).
    /// <para>
    /// Holds both <em>declared</em> bindings — names the referenced sequence declares — and
    /// <em>ad-hoc</em> values, names it does not. Ad-hoc values still enter the run scope and are
    /// inheritable by any command invoked at any depth beneath the entry, which is what lets an
    /// intermediate sequence pass a value through without re-declaring it. A supplied name that
    /// nothing in the entry's reachable chain consumes is reported as a non-blocking warning, so a
    /// typo is discoverable without preventing the run.
    /// </para>
    /// <para>
    /// Per entry and independent, exactly like <see cref="Enabled"/> and the timer fields: two entries
    /// referencing the same sequence hold separate collections. Templates persisted before this field
    /// existed omit it in JSON and deserialize as empty, preserving pre-feature behaviour.
    /// </para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Collection<ParameterBinding> ParameterValues => _parameterValues;

    /// <summary>
    /// JSON projection of <see cref="ParameterValues"/>, omitted entirely when empty so a template
    /// with no parameter values round-trips byte-identically to its pre-feature form.
    /// </summary>
    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonPropertyName("parameterValues")]
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Collection<ParameterBinding>? ParameterValuesWritable {
      get => _parameterValues.Count == 0 ? null : _parameterValues;
      private set {
        _parameterValues.Clear();
        if (value is null) return;
        foreach (var binding in value) _parameterValues.Add(binding);
      }
    }
  }
}
