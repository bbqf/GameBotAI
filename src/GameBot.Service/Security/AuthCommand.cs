using System.Globalization;

namespace GameBot.Service.Security;

/// <summary>
/// The local command mode of the service executable: <c>GameBot.Service.exe auth &lt;command&gt;</c> (feature 187, contracts/auth-cli.md).
/// It does not start the web server. It reads and writes the files in <c>data/auth/</c> directly.
/// </summary>
internal static class AuthCommand {
  internal const int ExitOk = 0;
  internal const int ExitFileError = 1;
  internal const int ExitUsage = 2;

  private const string Usage =
    "Usage: GameBot.Service.exe auth <command> [--data-dir <path>]\n"
    + "Commands:\n"
    + "  new-code           Make a registration code. It is valid for 10 minutes.\n"
    + "  list               Show the registered passkeys and the number of active sessions.\n"
    + "  remove <id>        Remove a passkey and end its sessions.\n"
    + "  end-sessions       End all sessions.";

  /// <summary>True when the arguments ask for the command mode.</summary>
  public static bool IsCommand(string[] args) =>
    args is { Length: > 0 } && string.Equals(args[0], "auth", StringComparison.OrdinalIgnoreCase);

  /// <summary>Runs the command. <paramref name="args"/> start with <c>auth</c>.</summary>
  public static int Run(string[] args, TextWriter output, TextWriter error, IConfiguration configuration, TimeProvider? time = null) {
    ArgumentNullException.ThrowIfNull(args);
    ArgumentNullException.ThrowIfNull(output);
    ArgumentNullException.ThrowIfNull(error);
    ArgumentNullException.ThrowIfNull(configuration);

    var rest = new List<string>(args.Skip(1));
    string? dataDir = null;
    var dirIndex = rest.FindIndex(a => string.Equals(a, "--data-dir", StringComparison.Ordinal));
    if (dirIndex >= 0) {
      if (dirIndex + 1 >= rest.Count) {
        return UsageError(error, "The option --data-dir needs a path.");
      }

      dataDir = rest[dirIndex + 1];
      rest.RemoveRange(dirIndex, 2);
    }

    if (rest.Count == 0 || string.Equals(rest[0], "--help", StringComparison.Ordinal) || string.Equals(rest[0], "-h", StringComparison.Ordinal)) {
      output.WriteLine(Usage);
      return ExitOk;
    }

    var root = dataDir ?? GameBotServiceSetup.ResolveStorageRoot(configuration);
    var clock = time ?? TimeProvider.System;
    var authDir = Path.Combine(root, "auth");
    try {
      return Dispatch(rest, authDir, configuration, clock, output, error);
    }
    catch (IOException ex) {
      error.WriteLine("File error: " + ex.Message);
      return ExitFileError;
    }
    catch (UnauthorizedAccessException ex) {
      error.WriteLine("File error: " + ex.Message);
      return ExitFileError;
    }
  }

  private static int Dispatch(
    List<string> rest, string authDir, IConfiguration configuration, TimeProvider clock, TextWriter output, TextWriter error) {
    var passkeys = new PasskeyStore(authDir, clock);
    var sessions = new SessionStore(authDir, passkeys, clock);
    var recorder = new SignInRecorder(authDir, clock);
    switch (rest[0].ToLowerInvariant()) {
      case "new-code" when rest.Count == 1:
        return NewCode(authDir, configuration, clock, output);
      case "list" when rest.Count == 1:
        return List(passkeys, sessions, output);
      case "remove" when rest.Count == 2:
        return Remove(rest[1], passkeys, sessions, recorder, output, error);
      case "end-sessions" when rest.Count == 1:
        sessions.EndAll();
        recorder.Record(SignInEvents.SessionsEnded, SignInRecorder.LocalCommandSource, null, "all");
        output.WriteLine("All sessions ended.");
        return ExitOk;
      default:
        return UsageError(error, "The command or its arguments are not valid.");
    }
  }

  private static int NewCode(string authDir, IConfiguration configuration, TimeProvider clock, TextWriter output) {
    var options = AuthOptions.Load(configuration);
    var code = new RegistrationCodeStore(authDir, clock).NewCode();
    output.WriteLine("Registration code: " + code);
    output.WriteLine("The code is valid for 10 minutes. You can use it one time.");
    output.WriteLine(options.PublicOrigin is null
      ? "Set GAMEBOT_AUTH_PUBLIC_ORIGIN first. Then open /register at the public address."
      : "Open this page on the new device: " + options.PublicOrigin.AbsoluteUri.TrimEnd('/') + "/register");
    if (!options.Enabled) {
      output.WriteLine("Warning: the sign-in function is off. Set GAMEBOT_AUTH_ENABLED=true.");
    }

    return ExitOk;
  }

  private static int List(PasskeyStore passkeys, SessionStore sessions, TextWriter output) {
    var items = passkeys.List();
    if (items.Count == 0) {
      output.WriteLine("No passkey is registered.");
    }

    foreach (var p in items) {
      var last = p.LastUsedUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "never";
      output.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{p.Id}  {p.Name}  registered {p.CreatedUtc:u}  last used {last}"));
    }

    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Active sessions: {sessions.ActiveCount}"));
    return ExitOk;
  }

  private static int Remove(
    string id, PasskeyStore passkeys, SessionStore sessions, SignInRecorder recorder, TextWriter output, TextWriter error) {
    if (!passkeys.Remove(id)) {
      error.WriteLine("No passkey has the id " + id + ".");
      return ExitUsage;
    }

    sessions.EndForPasskey(id);
    recorder.Record(SignInEvents.PasskeyRemoved, SignInRecorder.LocalCommandSource, id);
    output.WriteLine("Passkey " + id + " removed. Its sessions ended.");
    return ExitOk;
  }

  private static int UsageError(TextWriter error, string message) {
    error.WriteLine(message);
    error.WriteLine(Usage);
    return ExitUsage;
  }
}
