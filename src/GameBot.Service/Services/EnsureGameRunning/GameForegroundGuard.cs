namespace GameBot.Service.Services.EnsureGameRunning;

/// <summary>
/// Default <see cref="IGameForegroundGuard"/>, layered over the same
/// <see cref="IEnsureGameRunningActionHandler"/> the ensure-game-running step uses, so the guard
/// resolves the session's game and package exactly the way that step does.
///
/// The handler checks the foreground first and only issues a launch when the game is not in front,
/// so the common case (game already up) costs a single ADB foreground query and the confirm loop is
/// never entered. When the game IS out of the foreground the loop re-asks until the handler reports
/// it back or the confirm window elapses; re-issuing a LAUNCHER intent at each poll is harmless —
/// Android brings the existing task forward rather than restarting it, the same as tapping the app
/// icon twice.
/// </summary>
internal sealed class GameForegroundGuard : IGameForegroundGuard {
  /// <summary>
  /// How long to keep confirming after a launch. The case this exists for is a backgrounded game
  /// whose task is still alive, which returns within a poll or two; a genuine cold start will not
  /// finish inside this window, and deliberately is not waited out — the firing proceeds (and most
  /// likely fails, as it does today) and the NEXT firing's guard picks the recovery back up. That
  /// keeps a dead game from stalling the whole queue behind one sequence's watchdog.
  /// </summary>
  private static readonly TimeSpan DefaultConfirmTimeout = TimeSpan.FromSeconds(15);
  private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

  private readonly IEnsureGameRunningActionHandler _ensureGameRunning;
  private readonly TimeSpan _confirmTimeout;
  private readonly TimeSpan _pollInterval;
  private readonly TimeProvider _timeProvider;

  public GameForegroundGuard(
      IEnsureGameRunningActionHandler ensureGameRunning,
      TimeProvider? timeProvider = null,
      TimeSpan? confirmTimeout = null,
      TimeSpan? pollInterval = null) {
    _ensureGameRunning = ensureGameRunning;
    _timeProvider = timeProvider ?? TimeProvider.System;
    _confirmTimeout = confirmTimeout ?? DefaultConfirmTimeout;
    _pollInterval = pollInterval ?? DefaultPollInterval;
  }

  public async Task<GameForegroundGuardResult> EnsureForegroundAsync(string sessionId, CancellationToken ct = default) {
    var first = await _ensureGameRunning.ExecuteAsync(sessionId, ct).ConfigureAwait(false);
    if (first.Outcome == EnsureGameRunningOutcome.GameRunning) {
      return new GameForegroundGuardResult(GameForegroundGuardOutcome.AlreadyForeground, first.ReasonCode);
    }

    // Anything other than "not running" means the guard cannot act here (no queue context, no linked
    // game, no package name, non-Windows host). Report it as inapplicable so callers pass through
    // unchanged rather than treating a configuration gap as a device problem.
    if (first.Outcome != EnsureGameRunningOutcome.GameNotRunning) {
      return new GameForegroundGuardResult(GameForegroundGuardOutcome.NotApplicable, first.ReasonCode);
    }

    // The first call already issued the launch. Confirm it landed.
    var deadline = _timeProvider.GetUtcNow() + _confirmTimeout;
    var last = first;
    while (_timeProvider.GetUtcNow() < deadline) {
      await Task.Delay(_pollInterval, ct).ConfigureAwait(false);

      last = await _ensureGameRunning.ExecuteAsync(sessionId, ct).ConfigureAwait(false);
      if (last.Outcome == EnsureGameRunningOutcome.GameRunning) {
        return new GameForegroundGuardResult(GameForegroundGuardOutcome.Recovered, last.ReasonCode);
      }

      if (last.Outcome != EnsureGameRunningOutcome.GameNotRunning) {
        return new GameForegroundGuardResult(GameForegroundGuardOutcome.NotApplicable, last.ReasonCode);
      }
    }

    return new GameForegroundGuardResult(GameForegroundGuardOutcome.RecoveryFailed, last.ReasonCode);
  }
}
