using System.Text.Json.Serialization;

namespace GameBot.Domain.QueueTemplates {
  /// <summary>
  /// Determines when a sequence entry in a queue template is executed during a queue run.
  /// </summary>
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public enum ScheduleType {
    /// <summary>
    /// Default. Executes once per cycle in template order, as the normal queue step.
    /// Completion of all OncePerRun steps signals the end of a cycle (or the full run when
    /// cycle execution is disabled).
    /// </summary>
    OncePerRun = 0,

    /// <summary>
    /// Executes automatically after each OncePerRun sequence completes, and once more after the
    /// final OncePerRun step. Does not count toward run completion. Multiple EveryStep entries
    /// execute in their template order after each regular step.
    /// </summary>
    EveryStep = 1,

    /// <summary>
    /// Executes at most once per calendar day during the run. Evaluated at the start of each
    /// iteration (before OncePerRun steps): fires when the configured wall-clock time-of-day
    /// (server local time) has passed and the entry has not already fired on the current
    /// calendar date within this run. Resets on queue restart.
    /// </summary>
    Timer = 2
  }
}
