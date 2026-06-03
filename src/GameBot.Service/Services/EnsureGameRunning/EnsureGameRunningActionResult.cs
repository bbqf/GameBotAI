namespace GameBot.Service.Services.EnsureGameRunning;

internal enum EnsureGameRunningOutcome {
  /// <summary>Game was already the active foreground app — action succeeded.</summary>
  GameRunning,
  /// <summary>Game was not running; launch was attempted — action reports failure.</summary>
  GameNotRunning,
  /// <summary>Action was not executed within a queue context (no queue: session label).</summary>
  NoQueueContext,
  /// <summary>The queue has no linked game.</summary>
  NoLinkedGame,
  /// <summary>The linked game has no package name configured.</summary>
  NoPackageName,
  /// <summary>ADB operations are not available on this platform (non-Windows).</summary>
  PlatformUnsupported
}

internal sealed record EnsureGameRunningActionResult(EnsureGameRunningOutcome Outcome) {
  public bool IsSuccess => Outcome == EnsureGameRunningOutcome.GameRunning;

  public string ReasonCode => Outcome switch {
    EnsureGameRunningOutcome.GameRunning        => "game_running",
    EnsureGameRunningOutcome.GameNotRunning     => "game_not_running",
    EnsureGameRunningOutcome.NoQueueContext     => "no_queue_context",
    EnsureGameRunningOutcome.NoLinkedGame       => "no_linked_game",
    EnsureGameRunningOutcome.NoPackageName      => "no_package_name",
    _                                           => "platform_unsupported"
  };
}
