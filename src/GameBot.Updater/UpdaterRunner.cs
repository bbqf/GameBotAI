namespace GameBot.Updater;

/// <summary>
/// The steps of the updater (research R-007): wait for the bot to exit, run msiexec, start the bot,
/// write the result. Windows Installer rolls back by itself when msiexec fails, so the old bot works again.
/// </summary>
internal sealed class UpdaterRunner {
  public const int ExitSucceeded = 0;
  public const int ExitFailed = 1;
  public const int ExitBadArguments = 2;

  private readonly IUpdaterHost _host;
  private readonly TimeSpan _waitForBot;

  public UpdaterRunner(IUpdaterHost host, TimeSpan? waitForBot = null) {
    _host = host;
    _waitForBot = waitForBot ?? TimeSpan.FromSeconds(UpdaterArguments.WaitSecondsDefault);
  }

  public async Task<int> RunAsync(UpdaterArguments args) {
    ArgumentNullException.ThrowIfNull(args);

    // Step 1: the bot must exit, or the installer cannot replace the files in use.
    if (args.BotPid > 0 && !_host.WaitForExit(args.BotPid, _waitForBot)) {
      WriteFailed(args, UpdaterResult.InstallFailedCode,
        "The bot did not stop in time, so the update did not start.",
        "Try the update again. The old version still runs.",
        exitCode: null);
      return ExitFailed;
    }

    // Step 2: install.
    int exitCode;
    try {
      exitCode = await _host.RunMsiexecAsync(MsiexecArguments.Build(args)).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) {
      WriteFailed(args, UpdaterResult.InstallFailedCode,
        $"The installer did not start: {ex.Message}",
        "Check that Windows Installer works on this PC, then try again.",
        exitCode: null);
      StartBotQuietly(args);
      return ExitFailed;
    }

    if (!MsiexecArguments.IsSuccess(exitCode)) {
      var (message, hint) = MsiexecArguments.Describe(exitCode);
      WriteFailed(args, UpdaterResult.InstallFailedCode, message, hint, exitCode);
      // The installer rolled back, so the old bot is on disk. Start it.
      StartBotQuietly(args);
      return ExitFailed;
    }

    // Step 3: start the new bot. "succeeded" means that the new bot process started.
    Write(args, UpdaterResult.Restarting, finished: false, null, null, null, exitCode);
    if (!_host.StartBot(args.InstallFolder)) {
      WriteFailed(args, UpdaterResult.RestartFailedCode,
        "The update is installed, but the bot did not start.",
        "Start GameBot from the Start menu.",
        exitCode);
      return ExitFailed;
    }

    Write(args, UpdaterResult.Succeeded, finished: true, null, null, null, exitCode);
    return ExitSucceeded;
  }

  private void StartBotQuietly(UpdaterArguments args) {
    // The failure result is already on disk. A failed start cannot change it.
    _ = _host.StartBot(args.InstallFolder);
  }

  private void WriteFailed(UpdaterArguments args, string code, string message, string hint, int? exitCode) =>
    Write(args, UpdaterResult.Failed, finished: true, code, message, hint, exitCode);

  private void Write(UpdaterArguments args, string state, bool finished, string? code, string? message, string? hint, int? exitCode) {
    var result = new UpdaterResult {
      AttemptId = args.AttemptId,
      TargetVersion = args.TargetVersion,
      FromVersion = args.FromVersion,
      State = state,
      StartedAtUtc = args.StartedAtUtc,
      FinishedAtUtc = finished ? _host.UtcNow : null,
      ErrorCode = code,
      ErrorMessage = message,
      ErrorHint = hint,
      MsiexecExitCode = exitCode,
      LogPath = string.IsNullOrWhiteSpace(args.LogPath) ? null : args.LogPath
    };

    try {
      _host.WriteResultFile(args.ResultPath, result.ToJson());
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // Nothing more to do: the bot shows no result when the file cannot be written.
    }
  }
}
