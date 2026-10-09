using System.Globalization;
using GameBot.Domain.Updates;
using Microsoft.Extensions.Configuration;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Copies the updater files to <c>&lt;data root&gt;\updates\updater</c> and starts the copy detached
/// (research R-007). The copy lets the MSI replace the installed updater files. The bot passes its PID,
/// so the updater can wait for the bot to exit.
/// </summary>
internal sealed class UpdaterLauncher : IUpdaterLauncher {
  public const string UpdaterExeName = "GameBot.Updater.exe";

  private readonly UpdatePaths _paths;
  private readonly IUpdaterProcessStarter _starter;
  private readonly IConfiguration _configuration;
  private readonly string _installFolder;

  public UpdaterLauncher(UpdatePaths paths, IUpdaterProcessStarter starter, IConfiguration configuration)
    : this(paths, starter, configuration, AppContext.BaseDirectory) { }

  public UpdaterLauncher(UpdatePaths paths, IUpdaterProcessStarter starter, IConfiguration configuration, string installFolder) {
    _paths = paths;
    _starter = starter;
    _configuration = configuration;
    _installFolder = installFolder;
  }

  public void Launch(UpdaterLaunchRequest request) {
    ArgumentNullException.ThrowIfNull(request);
    var source = Path.Combine(_installFolder, "updater");
    if (!Directory.Exists(source)) {
      throw new UpdateFailureException(new UpdateError(
        "update_install_failed",
        "The updater program is missing from the install folder.",
        "Install GameBot again with the installer, then try the update."));
    }

    try {
      ReplaceDirectory(source, _paths.UpdaterCopyDirectory);
      var exe = Path.Combine(_paths.UpdaterCopyDirectory, UpdaterExeName);
      _starter.StartDetached(exe, BuildArguments(request), _paths.UpdaterCopyDirectory);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) {
      throw new UpdateFailureException(new UpdateError(
        "update_install_failed",
        "The bot cannot start the updater program.",
        "Close other programs that use the GameBot files, then try again."), ex);
    }
  }

  /// <summary>The command line for the updater (the names match UpdaterArguments in GameBot.Updater).</summary>
  internal IReadOnlyList<string> BuildArguments(UpdaterLaunchRequest request) {
    var args = new List<string> {
      "--bot-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
      "--msi", request.MsiPath,
      "--install-folder", _installFolder.TrimEnd('\\', '/'),
      "--data-root", _paths.DataRoot,
      "--result-path", _paths.ResultFile,
      "--log-path", _paths.MsiexecLogPath(request.AttemptId),
      "--attempt-id", request.AttemptId.ToString("D", CultureInfo.InvariantCulture),
      "--from-version", request.FromVersion,
      "--target-version", request.TargetVersion,
      "--started-at", request.StartedAtUtc.ToString("o", CultureInfo.InvariantCulture)
    };

    // The installer keeps the saved network values by itself. The bot passes known values too.
    var port = _configuration["Service:Network:Port"] ?? Environment.GetEnvironmentVariable("GAMEBOT_PORT");
    if (!string.IsNullOrWhiteSpace(port)) {
      args.Add("--port");
      args.Add(port);
    }
    var bindHost = _configuration["Service:Network:BindHost"] ?? Environment.GetEnvironmentVariable("GAMEBOT_BIND_HOST");
    if (!string.IsNullOrWhiteSpace(bindHost)) {
      args.Add("--bind-host");
      args.Add(bindHost);
    }

    return args;
  }

  private static void ReplaceDirectory(string source, string target) {
    if (Directory.Exists(target)) {
      Directory.Delete(target, recursive: true);
    }

    Directory.CreateDirectory(target);
    foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) {
      Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
    }
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
      File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }
  }
}
