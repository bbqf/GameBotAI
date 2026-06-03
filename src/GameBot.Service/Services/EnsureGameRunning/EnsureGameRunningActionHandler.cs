using GameBot.Domain.Games;
using GameBot.Domain.Queues;
using GameBot.Emulator.Session;

namespace GameBot.Service.Services.EnsureGameRunning;

internal sealed class EnsureGameRunningActionHandler : IEnsureGameRunningActionHandler {
  private const string QueueSessionPrefix = "queue:";

  private readonly ISessionManager _sessions;
  private readonly IQueueRepository _queues;
  private readonly IGameRepository _games;
  private readonly IAdbGameOperations _adb;

  public EnsureGameRunningActionHandler(
    ISessionManager sessions,
    IQueueRepository queues,
    IGameRepository games,
    IAdbGameOperations adb) {
    _sessions = sessions;
    _queues = queues;
    _games = games;
    _adb = adb;
  }

  public async Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default) {
    // 1. Resolve session
    var session = _sessions.GetSession(sessionId);
    if (session is null)
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.NoPackageName);

    // 2. Platform guard — must come before any ADB call
    if (!OperatingSystem.IsWindows())
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.PlatformUnsupported);

    // 3. Resolve the game:
    //    - Queue context (label = "queue:{id}"): resolve game via queue's LinkedGameId
    //    - Direct session (label = game ID): use the session label as the game ID directly
    GameArtifact? game;
    var queueId = ExtractQueueId(session.GameId);
    if (queueId is not null) {
      var queue = await _queues.GetAsync(queueId).ConfigureAwait(false);
      if (queue is null || string.IsNullOrEmpty(queue.LinkedGameId))
        return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.NoLinkedGame);
      game = await _games.GetAsync(queue.LinkedGameId, ct).ConfigureAwait(false);
    }
    else {
      // Direct session: session.GameId is the game ID
      game = await _games.GetAsync(session.GameId, ct).ConfigureAwait(false);
    }

    // 4. Validate package name
    if (game is null || string.IsNullOrEmpty(game.PackageName))
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.NoPackageName);

    // 6. Check foreground app
    var foreground = await _adb.GetForegroundPackageAsync(session.DeviceSerial ?? string.Empty, ct).ConfigureAwait(false);
    if (string.Equals(foreground, game.PackageName, StringComparison.OrdinalIgnoreCase))
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning);

    // 7. Game not running — launch (best effort) and report failure
    await _adb.LaunchAppAsync(session.DeviceSerial ?? string.Empty, game.PackageName, ct).ConfigureAwait(false);
    return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameNotRunning);
  }

  private static string? ExtractQueueId(string? sessionLabel) {
    if (sessionLabel is null) return null;
    return sessionLabel.StartsWith(QueueSessionPrefix, StringComparison.OrdinalIgnoreCase)
      ? sessionLabel[QueueSessionPrefix.Length..]
      : null;
  }
}
