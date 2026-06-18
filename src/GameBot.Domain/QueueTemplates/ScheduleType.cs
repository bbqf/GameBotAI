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
    /// Displayed to operators as "After Every Step"; the stored/wire identifier remains
    /// <c>EveryStep</c> for backward compatibility.
    /// </summary>
    EveryStep = 1,

    /// <summary>
    /// Executes at most once per calendar day during the run. Evaluated at the start of each
    /// iteration (before OncePerRun steps): fires when the configured wall-clock time-of-day
    /// (server local time) has passed and the entry has not already fired on the current
    /// calendar date within this run. Resets on queue restart.
    /// </summary>
    Timer = 2,

    /// <summary>
    /// Executes once at the start of the run, in template order, before any timer evaluation and
    /// before the first OncePerRun step. Runs once per run (not per cycle) and counts toward the
    /// run's executed total. A failure is non-fatal (recorded in failed; the run continues),
    /// consistent with OncePerRun handling.
    /// </summary>
    AtQueueStart = 3
  }
}
