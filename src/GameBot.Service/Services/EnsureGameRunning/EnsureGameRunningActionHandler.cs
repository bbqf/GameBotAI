using System.Diagnostics;
using GameBot.Domain.Games;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;

namespace GameBot.Service.Services.EnsureGameRunning;

internal sealed class EnsureGameRunningActionHandler : IEnsureGameRunningActionHandler {
  private const string QueueSessionPrefix = "queue:";

  private readonly ISessionManager _sessions;
  private readonly IQueueRepository _queues;
  private readonly IGameRepository _games;
  private readonly IAdbGameOperations _adb;
  private readonly EnsureGameRunningRestartOptions _options;

  public EnsureGameRunningActionHandler(
    ISessionManager sessions,
    IQueueRepository queues,
    IGameRepository games,
    IAdbGameOperations adb,
    EnsureGameRunningRestartOptions? options = null) {
    _sessions = sessions;
    _queues = queues;
    _games = games;
    _adb = adb;
    _options = options ?? new EnsureGameRunningRestartOptions();
  }

  public async Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default) {
    var (failure, session, game) = await ResolveAsync(sessionId, ct).ConfigureAwait(false);
    if (failure is not null) return failure;

    // 4. Validate package name
    if (string.IsNullOrEmpty(game!.PackageName))
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.NoPackageName);

    // 6. Check foreground app
    var foreground = await _adb.GetForegroundPackageAsync(session!.DeviceSerial ?? string.Empty, ct).ConfigureAwait(false);
    if (string.Equals(foreground, game.PackageName, StringComparison.OrdinalIgnoreCase))
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning);

    // 7. Game not running — launch (best effort) and report failure
    await _adb.LaunchAppAsync(session.DeviceSerial ?? string.Empty, game.PackageName, ct).ConfigureAwait(false);
    return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameNotRunning);
  }

  public async Task<EnsureGameRunningActionResult> RestartAsync(string sessionId, CancellationToken ct = default) {
    var (failure, session, game) = await ResolveAsync(sessionId, ct).ConfigureAwait(false);
    if (failure is not null) return failure;

    // An unsafe package name goes into an adb shell command, so reject it before any device call.
    var package = game!.PackageName;
    if (!AdbGameOperations.IsSafePackageName(package))
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.NoPackageName);

    // Use the device of the session only. A blank serial makes adb pick any device.
    var serial = session!.DeviceSerial;
    if (string.IsNullOrWhiteSpace(serial))
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.RestartNoDevice);

    // Stop
    var stop = await RunLimitedAsync(c => _adb.ForceStopAppAsync(serial, package!, c), _options.RestartStopTimeout, ct)
      .ConfigureAwait(false);
    if (!stop.Completed || !stop.Value)
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.RestartStopFailed);

    // Settle
    if (_options.RestartSettleDelay > TimeSpan.Zero)
      await Task.Delay(_options.RestartSettleDelay, ct).ConfigureAwait(false);

    // Start
    var start = await RunLimitedAsync(c => _adb.TryLaunchAppAsync(serial, package!, c), _options.RestartStartTimeout, ct)
      .ConfigureAwait(false);
    if (!start.Completed || !start.Value)
      return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.RestartStartFailed);

    // Wait for the foreground
    var clock = Stopwatch.StartNew();
    while (true) {
      ct.ThrowIfCancellationRequested();
      var remaining = _options.RestartForegroundWait - clock.Elapsed;
      if (remaining <= TimeSpan.Zero)
        return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.RestartForegroundTimeout);

      var probeLimit = remaining < _options.RestartProbeTimeout ? remaining : _options.RestartProbeTimeout;
      var probe = await RunLimitedAsync(c => _adb.GetForegroundPackageAsync(serial, c), probeLimit, ct).ConfigureAwait(false);
      // A probe that fails or does not end in time is a miss. The poll goes on.
      if (probe.Completed && string.Equals(probe.Value, package, StringComparison.OrdinalIgnoreCase))
        return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.Restarted);

      remaining = _options.RestartForegroundWait - clock.Elapsed;
      if (remaining <= TimeSpan.Zero)
        return new EnsureGameRunningActionResult(EnsureGameRunningOutcome.RestartForegroundTimeout);
      var pause = remaining < _options.RestartPollInterval ? remaining : _options.RestartPollInterval;
      await Task.Delay(pause, ct).ConfigureAwait(false);
    }
  }

  /// <summary>
  /// Runs one device call under a time limit. A limit, an exception from the call, or a call that
  /// ignores the cancel gives <c>Completed = false</c>. A cancel from the caller throws
  /// <see cref="OperationCanceledException"/>.
  /// </summary>
  private static async Task<(bool Completed, T? Value)> RunLimitedAsync<T>(
    Func<CancellationToken, Task<T>> call, TimeSpan limit, CancellationToken ct) {
    ct.ThrowIfCancellationRequested();
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linked.CancelAfter(limit);
    Task<T> task;
    try {
      task = call(linked.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
      return (false, default);
    }
    catch (Exception) when (!ct.IsCancellationRequested) {
      return (false, default);
    }

    var finished = await Task.WhenAny(task, Task.Delay(limit, ct)).ConfigureAwait(false);
    if (ct.IsCancellationRequested) {
      ObserveFault(task);
      throw new OperationCanceledException(ct);
    }
    if (!ReferenceEquals(finished, task)) {
      await linked.CancelAsync().ConfigureAwait(false);
      ObserveFault(task);
      return (false, default);
    }

    try {
      return (true, await task.ConfigureAwait(false));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
      throw;
    }
    catch (Exception) {
      return (false, default);
    }
  }

  private static void ObserveFault(Task task) =>
    task.ContinueWith(
      static t => _ = t.Exception,
      CancellationToken.None,
      TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
      TaskScheduler.Default);

  // Steps 1 to 3 of both paths: the session, the platform guard, and the game.
  private async Task<(EnsureGameRunningActionResult? Failure, EmulatorSession? Session, GameArtifact? Game)> ResolveAsync(
    string sessionId, CancellationToken ct) {
    // 1. Resolve session
    var session = _sessions.GetSession(sessionId);
    if (session is null)
      return (new EnsureGameRunningActionResult(EnsureGameRunningOutcome.NoQueueContext), null, null);

    // 2. Platform guard — must come before any ADB call
    if (!OperatingSystem.IsWindows())
      return (new EnsureGameRunningActionResult(EnsureGameRunningOutcome.PlatformUnsupported), null, null);

    // 3. Resolve the game:
    //    - Queue context (label = "queue:{id}"): resolve game via queue's LinkedGameId
    //    - Direct session (label = game ID): use the session label as the game ID directly
    GameArtifact? game;
    var queueId = ExtractQueueId(session.GameId);
    if (queueId is not null) {
      var queue = await _queues.GetAsync(queueId).ConfigureAwait(false);
      if (queue is null || string.IsNullOrEmpty(queue.LinkedGameId))
        return (new EnsureGameRunningActionResult(EnsureGameRunningOutcome.NoLinkedGame), null, null);
      game = await _games.GetAsync(queue.LinkedGameId, ct).ConfigureAwait(false);
    }
    else {
      // Direct session: session.GameId is the game ID
      game = await _games.GetAsync(session.GameId, ct).ConfigureAwait(false);
    }

    if (game is null)
      return (new EnsureGameRunningActionResult(EnsureGameRunningOutcome.NoPackageName), null, null);
    return (null, session, game);
  }

  private static string? ExtractQueueId(string? sessionLabel) {
    if (sessionLabel is null) return null;
    return sessionLabel.StartsWith(QueueSessionPrefix, StringComparison.OrdinalIgnoreCase)
      ? sessionLabel[QueueSessionPrefix.Length..]
      : null;
  }
}
