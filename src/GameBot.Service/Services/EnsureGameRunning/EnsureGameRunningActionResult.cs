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
  PlatformUnsupported,
  /// <summary>The game was stopped, started again, and reached the foreground (feature 129).</summary>
  Restarted,
  /// <summary>The session has no device serial, so the restart made no device call.</summary>
  RestartNoDevice,
  /// <summary>The stop call failed or did not end in time.</summary>
  RestartStopFailed,
  /// <summary>The start call failed or did not end in time.</summary>
  RestartStartFailed,
  /// <summary>The game did not reach the foreground in time after the start.</summary>
  RestartForegroundTimeout
}

internal sealed record EnsureGameRunningActionResult(EnsureGameRunningOutcome Outcome) {
  public bool IsSuccess => Outcome is EnsureGameRunningOutcome.GameRunning or EnsureGameRunningOutcome.Restarted;

  public string ReasonCode => Outcome switch {
    EnsureGameRunningOutcome.GameRunning              => "game_running",
    EnsureGameRunningOutcome.GameNotRunning           => "game_not_running",
    EnsureGameRunningOutcome.NoQueueContext           => "no_queue_context",
    EnsureGameRunningOutcome.NoLinkedGame             => "no_linked_game",
    EnsureGameRunningOutcome.NoPackageName            => "no_package_name",
    EnsureGameRunningOutcome.Restarted                => "restarted",
    EnsureGameRunningOutcome.RestartNoDevice          => "restart_no_device",
    EnsureGameRunningOutcome.RestartStopFailed        => "restart_stop_failed",
    EnsureGameRunningOutcome.RestartStartFailed       => "restart_start_failed",
    EnsureGameRunningOutcome.RestartForegroundTimeout => "restart_foreground_timeout",
    _                                                 => "platform_unsupported"
  };
}
