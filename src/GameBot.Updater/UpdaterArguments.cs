using System.Globalization;

namespace GameBot.Updater;

/// <summary>
/// The command line of the updater. All values come as "--name value" pairs.
/// The bot builds the command line (UpdaterLauncher).
/// </summary>
internal sealed record UpdaterArguments(
  int BotPid,
  string MsiPath,
  string InstallFolder,
  string Port,
  string BindHost,
  string DataRoot,
  string ResultPath,
  string AttemptId,
  string FromVersion,
  string TargetVersion,
  DateTimeOffset StartedAtUtc,
  string LogPath) {
  public const int WaitSecondsDefault = 60;

  /// <summary>Parse the command line. Return null and set <paramref name="error"/> when a value is missing or not valid.</summary>
  public static UpdaterArguments? TryParse(IReadOnlyList<string> args, out string? error) {
    ArgumentNullException.ThrowIfNull(args);
    error = null;
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Count; i++) {
      var name = args[i];
      if (!name.StartsWith("--", StringComparison.Ordinal)) {
        error = $"Unexpected argument '{name}'.";
        return null;
      }
      if (i + 1 >= args.Count) {
        error = $"Argument '{name}' has no value.";
        return null;
      }

      map[name.Substring(2)] = args[++i];
    }

    foreach (var required in new[] { "bot-pid", "msi", "install-folder", "result-path", "attempt-id", "target-version" }) {
      if (!map.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value)) {
        error = $"Argument '--{required}' is required.";
        return null;
      }
    }

    if (!int.TryParse(map["bot-pid"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid < 0) {
      error = "Argument '--bot-pid' must be a whole number.";
      return null;
    }

    var startedAt = DateTimeOffset.UtcNow;
    if (map.TryGetValue("started-at", out var startedText) &&
        !DateTimeOffset.TryParse(startedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out startedAt)) {
      error = "Argument '--started-at' must be a date and time.";
      return null;
    }

    return new UpdaterArguments(
      pid,
      map["msi"],
      map["install-folder"],
      map.GetValueOrDefault("port", string.Empty),
      map.GetValueOrDefault("bind-host", string.Empty),
      map.GetValueOrDefault("data-root", string.Empty),
      map["result-path"],
      map["attempt-id"],
      map.GetValueOrDefault("from-version", string.Empty),
      map["target-version"],
      startedAt,
      map.GetValueOrDefault("log-path", string.Empty));
  }
}
