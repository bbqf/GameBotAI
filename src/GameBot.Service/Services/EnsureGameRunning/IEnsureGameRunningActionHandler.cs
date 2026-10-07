namespace GameBot.Service.Services.EnsureGameRunning;

internal interface IEnsureGameRunningActionHandler {
  Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default);

  /// <summary>
  /// Stops the game, starts it again, and waits for the foreground (feature 129).
  /// Returns <see cref="EnsureGameRunningOutcome.Restarted"/> or a failure result with its own reason code.
  /// </summary>
  Task<EnsureGameRunningActionResult> RestartAsync(string sessionId, CancellationToken ct = default);
}
