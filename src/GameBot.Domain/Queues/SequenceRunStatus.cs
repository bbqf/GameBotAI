namespace GameBot.Domain.Queues;

/// <summary>
/// The status of one completed sequence run that a queue started (feature 105). The store and the API
/// write the value as lower-case text: <c>success</c>, <c>failure</c> or <c>cancelled</c>.
/// </summary>
public enum SequenceRunStatus {
  /// <summary>The run completed without a failure. A run that a Break step ends is a success.</summary>
  Success,

  /// <summary>The run ended with an error or a failed step.</summary>
  Failure,

  /// <summary>
  /// The queue stopped the run: a stop by hand, a failure-policy stop, or the sequence time limit
  /// (watchdog).
  /// </summary>
  Cancelled
}
