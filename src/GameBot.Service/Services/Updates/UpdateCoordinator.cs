using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;

namespace GameBot.Service.Services.Updates;

/// <summary>The result of a request to start an install.</summary>
internal enum InstallStartOutcome {
  Started,
  NotAvailable,
  InProgress,
  DiskSpace
}

internal readonly record struct InstallStartResult(InstallStartOutcome Outcome, UpdateAttempt? Attempt, string? Message);

/// <summary>
/// Runs the install flow of one update attempt (research R-007, R-010). It is a singleton with one gate,
/// so only one attempt can be active. The steps are: download, verify, write the result file, start the
/// updater, ask the host to stop, stop the queues. The updater program does the rest after the bot exits.
/// </summary>
internal sealed class UpdateCoordinator : IDisposable {
  // Free space must be at least 3 times the MSI size (research R-011).
  internal const int DiskSpaceFactor = 3;

  private readonly UpdateCheckService _check;
  private readonly IUpdateDownloader _downloader;
  private readonly IUpdaterLauncher _launcher;
  private readonly IUpdateResultStore _store;
  private readonly IInstalledVersionProvider _installed;
  private readonly IUpdateQueueStopper _queues;
  private readonly IFreeSpaceProvider _space;
  private readonly UpdatePaths _paths;
  private readonly IHostApplicationLifetime _lifetime;
  private readonly TimeProvider _time;
  private readonly ILogger<UpdateCoordinator> _logger;
  private readonly SemaphoreSlim _gate = new(1, 1);

  private volatile UpdateAttempt? _attempt;
  private Task _running = Task.CompletedTask;

  public UpdateCoordinator(
      UpdateCheckService check,
      IUpdateDownloader downloader,
      IUpdaterLauncher launcher,
      IUpdateResultStore store,
      IInstalledVersionProvider installed,
      IUpdateQueueStopper queues,
      IFreeSpaceProvider space,
      UpdatePaths paths,
      IHostApplicationLifetime lifetime,
      TimeProvider time,
      ILogger<UpdateCoordinator> logger) {
    _check = check;
    _downloader = downloader;
    _launcher = launcher;
    _store = store;
    _installed = installed;
    _queues = queues;
    _space = space;
    _paths = paths;
    _lifetime = lifetime;
    _time = time;
    _logger = logger;
  }

  /// <summary>The newest attempt in this bot run. Null before the first install request.</summary>
  public UpdateAttempt? CurrentAttempt => _attempt;

  /// <summary>True while an attempt runs (it holds the gate).</summary>
  public bool IsInstallActive => _gate.CurrentCount == 0;

  /// <summary>The task of the newest attempt. Tests await it.</summary>
  internal Task Running => _running;

  /// <summary>
  /// Check the request and start the attempt in the background. The checks are the steps 4 to 6 of the
  /// install route: the version, the gate, and the disk space.
  /// </summary>
  public InstallStartResult TryStartInstall(string targetVersion) {
    var release = _check.LastRelease;
    var installed = _installed.GetInstalledVersion();
    if (release is null
        || !SemanticVersion.TryParse(targetVersion, out var target)
        || target != release.Version
        || !UpdateVersionSelector.IsUpdateAvailable(installed, target)) {
      return new InstallStartResult(InstallStartOutcome.NotAvailable, null, "The version is not the newest version that the last check found.");
    }

    if (!_gate.Wait(0)) {
      return new InstallStartResult(InstallStartOutcome.InProgress, null, "An update is already in progress.");
    }

    var started = false;
    try {
      Directory.CreateDirectory(_paths.Directory);
      var free = _space.GetFreeBytes(_paths.Directory);
      if (free < release.MsiSizeBytes * DiskSpaceFactor) {
        return new InstallStartResult(InstallStartOutcome.DiskSpace, null, "There is not enough free disk space for the update.");
      }

      var attempt = new UpdateAttempt {
        AttemptId = Guid.NewGuid(),
        TargetVersion = release.Version.ToString(),
        FromVersion = installed.ToString(),
        State = UpdateState.Downloading,
        StartedAtUtc = _time.GetUtcNow()
      };
      _attempt = attempt;
      UpdateLog.InstallStarted(_logger, attempt.AttemptId, attempt.FromVersion, attempt.TargetVersion);

      _running = Task.Run(() => RunAsync(release, attempt));
      started = true;
      return new InstallStartResult(InstallStartOutcome.Started, attempt, null);
    }
    finally {
      if (!started) {
        _gate.Release();
      }
    }
  }

  private async Task RunAsync(ReleaseInfo release, UpdateAttempt attempt) {
    var ct = _lifetime.ApplicationStopping;
    var launched = false;
    try {
      var msiPath = await _downloader.DownloadAsync(release, ct).ConfigureAwait(false);

      SetState(UpdateState.Verifying);
      await _downloader.VerifyAsync(msiPath, release, ct).ConfigureAwait(false);

      SetState(UpdateState.Installing);
      await _store.WriteAsync(_attempt!, ct).ConfigureAwait(false);
      _launcher.Launch(new UpdaterLaunchRequest(
        attempt.AttemptId, msiPath, attempt.FromVersion, attempt.TargetVersion, attempt.StartedAtUtc));
      launched = true;
    }
    catch (UpdateFailureException ex) {
      Fail(ex.Error);
    }
    catch (OperationCanceledException) {
      Fail(new UpdateError("update_download_failed", "The update stopped because the bot is stopping.", "Start the update again."));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      Fail(new UpdateError("update_install_failed", $"The bot cannot write the update files: {ex.Message}", "Check the free disk space and the rights on the data folder, then try again."));
    }

    if (!launched) {
      return;
    }

    // The updater waits for the bot to exit. Stop the host first. Then the queue engine sees the
    // stopping token and keeps the "running" record of each queue. This record starts the queues again.
    _lifetime.StopApplication();
    try {
      await _queues.StopAllAsync(CancellationToken.None).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      UpdateLog.QueueStopFailed(_logger, ex.Message);
    }
  }

  public void Dispose() => _gate.Dispose();

  private void SetState(UpdateState state) {
    var current = _attempt!;
    _attempt = current with { State = state };
    UpdateLog.InstallState(_logger, current.AttemptId, state);
  }

  private void Fail(UpdateError error) {
    var current = _attempt!;
    var failed = current with {
      State = UpdateState.Failed,
      FinishedAtUtc = _time.GetUtcNow(),
      ErrorCode = error.Code,
      ErrorMessage = error.Message,
      ErrorHint = error.Hint
    };
    _attempt = failed;
    UpdateLog.InstallFailed(_logger, current.AttemptId, error.Code, error.Message);

    // Remove a result file with the state "installing" that an earlier step wrote. The failure is
    // in memory for the status route. A file would show the same failure again after a restart.
    _store.MarkReported();

    _gate.Release();
  }
}
