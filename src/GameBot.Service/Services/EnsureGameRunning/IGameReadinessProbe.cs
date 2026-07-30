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
  Task<GameReadinessResult> WaitUntilReadyAsync(DetectionTarget readinessImage, int timeoutMs, CancellationToken ct = default);
}
