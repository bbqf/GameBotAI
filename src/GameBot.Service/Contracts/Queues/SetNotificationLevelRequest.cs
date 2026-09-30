namespace GameBot.Service.Contracts.Queues {
  /// <summary>Request body of <c>PUT /api/queues/{id}/notification-level</c> (feature 120).</summary>
  internal sealed class SetNotificationLevelRequest {
    /// <summary>One of <c>none</c>, <c>failure</c> or <c>successAndFailure</c>. Required.</summary>
    public string? Level { get; set; }
  }
}
