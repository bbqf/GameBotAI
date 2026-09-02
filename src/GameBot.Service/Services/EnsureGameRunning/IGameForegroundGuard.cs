namespace GameBot.Service.Services.EnsureGameRunning;

/// <summary>What a foreground guard pass did about the game's foreground state.</summary>
internal enum GameForegroundGuardOutcome {
  /// <summary>The game was already the foreground app; nothing was done.</summary>
  AlreadyForeground,
  /// <summary>The game was not in front; a launch was issued and the game came back within the confirm window.</summary>
  Recovered,
  /// <summary>The game was not in front and had not returned when the confirm window elapsed.</summary>
  RecoveryFailed,
  /// <summary>
  /// The guard does not apply here — no queue context, no linked game, no package name configured,
  /// or ADB is unavailable on this host. Callers treat this exactly like a pass.
  /// </summary>
  NotApplicable
}

internal sealed record GameForegroundGuardResult(GameForegroundGuardOutcome Outcome, string ReasonCode) {
  /// <summary>True when the guard did real recovery work — the only outcome worth reporting.</summary>
  public bool Recovered => Outcome == GameForegroundGuardOutcome.Recovered;

  /// <summary>True when the game was verified absent from the foreground and did not come back.</summary>
  public bool Failed => Outcome == GameForegroundGuardOutcome.RecoveryFailed;
}

/// <summary>
/// Keeps the linked game in the foreground across a long-lived queue run.
///
/// A queue run binds one emulator for hours. Anything can push the game out of the foreground in
/// that window — a recovery loop pressing BACK off the game's top-level screen, a crash, the user
/// touching the emulator — and once that happens every subsequent image detection sees the device
/// launcher instead of the game. Sequences then fail forever while the queue still reports
/// "Running", because the only step that launches the game (a connect / ensure-game-running step)
/// normally runs once at queue start and never again.
///
/// This guard closes that hole: the queue asks it to confirm the foreground before each firing, so
/// a game that drops out is brought back at the next firing instead of never.
/// </summary>
internal interface IGameForegroundGuard {
  /// <summary>
  /// Confirms the session's linked game is the foreground app, launching it and waiting for it to
  /// come back when it is not. Never throws for an unusable context — that surfaces as
  /// <see cref="GameForegroundGuardOutcome.NotApplicable"/>.
  /// </summary>
  Task<GameForegroundGuardResult> EnsureForegroundAsync(string sessionId, CancellationToken ct = default);
}
