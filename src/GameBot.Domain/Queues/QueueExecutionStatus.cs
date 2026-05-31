namespace GameBot.Domain.Queues {
  /// <summary>
  /// Runtime execution status of an emulator execution queue.
  /// <c>Stopped</c> is the canonical representation of the spec's "not running" state
  /// and is the default/reset value after a service restart.
  /// </summary>
  public enum QueueExecutionStatus {
    Stopped,
    Running
  }
}
