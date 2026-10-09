namespace GameBot.Updater;

/// <summary>Builds the msiexec command line and maps msiexec exit codes to messages (research R-002).</summary>
internal static class MsiexecArguments {
  public const int Success = 0;
  public const int SuccessRebootRequired = 3010;

  /// <summary>
  /// Build: /i "msi" /qn /norestart /l*v "log" APPLICATIONFOLDER="folder" MSIINSTALLPERUSER=1,
  /// then PORT and BIND_HOST when the bot gives a value.
  /// </summary>
  public static string Build(UpdaterArguments args) {
    ArgumentNullException.ThrowIfNull(args);
    var parts = new List<string> {
      "/i",
      Quote(args.MsiPath),
      "/qn",
      "/norestart"
    };

    if (!string.IsNullOrWhiteSpace(args.LogPath)) {
      parts.Add("/l*v");
      parts.Add(Quote(args.LogPath));
    }

    parts.Add($"APPLICATIONFOLDER={Quote(args.InstallFolder.TrimEnd('\\', '/'))}");
    parts.Add("MSIINSTALLPERUSER=1");
    if (!string.IsNullOrWhiteSpace(args.Port)) {
      parts.Add($"PORT={Quote(args.Port)}");
    }
    if (!string.IsNullOrWhiteSpace(args.BindHost)) {
      parts.Add($"BIND_HOST={Quote(args.BindHost)}");
    }

    return string.Join(' ', parts);
  }

  public static bool IsSuccess(int exitCode) => exitCode is Success or SuccessRebootRequired;

  /// <summary>A message and a fix hint for the common msiexec exit codes.</summary>
  public static (string Message, string Hint) Describe(int exitCode) => exitCode switch {
    1603 => (
      "The installer stopped with a fatal error (code 1603).",
      "Close other programs that use the GameBot files, then try again. Read the installer log for details."),
    1618 => (
      "Another install is in progress (code 1618).",
      "Wait for the other install to end, then try again."),
    1925 => (
      "The installer has no right to change the install folder (code 1925).",
      "Run the update again from an account that owns the install folder."),
    _ => (
      $"The installer stopped with code {exitCode}.",
      "Read the installer log for details, then try again.")
  };

  private static string Quote(string value) => "\"" + value + "\"";
}
