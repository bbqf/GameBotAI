namespace GameBot.Service.Hosted;

internal sealed class TriggerWorkerOptions {
  // Base cadence in seconds when actively evaluating
  public int IntervalSeconds { get; set; } = 2;

  // Optional gameId filter to restrict evaluations
  public string? GameFilter { get; set; }

  // If true, when there are no active sessions, skip evaluation and use IdleBackoffSeconds delay
  public bool SkipWhenNoSessions { get; set; } = true;

  // Delay in seconds when idle (no sessions) to reduce CPU usage
  public int IdleBackoffSeconds { get; set; } = 5;
}
