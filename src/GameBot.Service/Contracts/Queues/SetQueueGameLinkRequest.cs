namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request body for setting or clearing a queue's linked game.
  /// <see cref="GameId"/> is the stable game ID to link, or null to clear the link.
  /// </summary>
  internal sealed class SetQueueGameLinkRequest {
    public string? GameId { get; set; }
  }
}
