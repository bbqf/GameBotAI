namespace GameBot.Domain.Queues {
  /// <summary>
  /// What a queue's failure policy does when its consecutive-failed-cycle threshold is reached
  /// (feature 087, issue #181).
  /// <para>
  /// <b>Notify is the primary action, not stop.</b> A cycling roster can self-heal — during the
  /// 2026-09-14 outage one relaunched the game on its own once the network returned — so an
  /// automatic halt is a liability where an alert is a guardrail. Stop and pause exist for rosters
  /// whose device should stop being touched, and are the operator's explicit choice.
  /// </para>
  /// </summary>
  public enum QueueFailureAction {
    /// <summary>Deliver an outbound notification. The run continues, and may still recover.</summary>
    Notify,

    /// <summary>
    /// End the run. Its terminating execution-log record carries
    /// <see cref="QueueStopReason.StoppedByFailurePolicy"/>. No notification is sent.
    /// </summary>
    Stop,

    /// <summary>
    /// Park the run: it stays alive and registered, holds its device session, and fires nothing
    /// until an operator resumes it. No notification is sent.
    /// </summary>
    Pause,

    /// <summary>
    /// Deliver a notification and then end the run. The notification is started before the
    /// cancellation so the alert is not cancelled by the stop it announces.
    /// </summary>
    NotifyAndStop
  }
}
