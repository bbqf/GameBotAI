using GameBot.Domain.Commands;

namespace GameBot.Service.Services.EnsureGameRunning;

/// <summary>Result of a game-readiness poll: whether the readiness image was detected, plus the
/// reference-image load status for diagnostics (<c>loaded</c>, <c>missing</c>, <c>unavailable</c>).</summary>
internal readonly record struct GameReadinessResult(bool Ready, string ImageLoadStatus);

/// <summary>
/// Polls the live emulator screen for a readiness image after a game launch, so the
/// <c>ensure-game-running</c> step only reports success once the game has actually reached the
/// configured ready screen (rather than as soon as its package is foreground).
/// </summary>
internal interface IGameReadinessProbe {
  /// <summary>Polls until the readiness image is detected or the timeout elapses.</summary>
  /// <param name="readinessImage">The reference image that means "the game is ready".</param>
  /// <param name="timeoutMs">How long to keep polling, in milliseconds.</param>
  /// <param name="sessionId">
  /// The emulator session to observe (feature 079). When supplied, the probe reads only that
  /// session's screen, so a concurrent queue run's device can never satisfy this gate. Null keeps
  /// the ambient/single-session resolution.
  /// </param>
  /// <param name="ct">Cancellation token.</param>
  Task<GameReadinessResult> WaitUntilReadyAsync(DetectionTarget readinessImage, int timeoutMs, string? sessionId = null, CancellationToken ct = default);
}
