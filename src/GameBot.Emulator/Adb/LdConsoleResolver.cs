using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GameBot.Emulator.Adb;

/// <summary>
/// Locates LDPlayer's <c>ldconsole.exe</c> (a.k.a. <c>dnconsole.exe</c>) command-line manager, used by
/// the ensure-emulator-running action (feature 070) to query/start/restart instances. Mirrors
/// <see cref="AdbResolver"/> because <c>ldconsole.exe</c> lives in the same LDPlayer install directory
/// as <c>adb.exe</c>; only the filename and env-override name differ. Returns <c>null</c> when not
/// found, which the caller treats as "emulator control unavailable" (neutral degradation).
/// </summary>
public static class LdConsoleResolver {
  [SupportedOSPlatform("windows")]
  public static string? ResolveLdConsolePath() {
    // 1) Explicit override
    var envOverride = Environment.GetEnvironmentVariable("GAMEBOT_LDCONSOLE_PATH");
    if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
      return envOverride;

    // 2) LDPlayer env hints
    var ldpHome = Environment.GetEnvironmentVariable("LDPLAYER_HOME") ?? Environment.GetEnvironmentVariable("LDP_HOME");
    if (!string.IsNullOrWhiteSpace(ldpHome)) {
      var candidate = FirstExisting(ldpHome);
      if (candidate is not null) return candidate;
    }

    // 2b) Common install paths (directories that also hold adb.exe)
    var dirs = new[]
    {
      @"C:\Program Files\LDPlayer\LDPlayer9",
      @"C:\LDPlayer\LDPlayer9",
      @"C:\LDPlayer4\LDPlayer",
      @"C:\Program Files\LDPlayer\LDPlayer8",
      @"C:\Program Files\LDPlayer",
      @"C:\LDPlayer"
    };
    foreach (var d in dirs) {
      var candidate = FirstExisting(d);
      if (candidate is not null) return candidate;
    }

    // 2c) Registry uninstall entries
    try {
      using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
      if (key != null) {
        foreach (var sub in key.GetSubKeyNames()) {
          using var subKey = key.OpenSubKey(sub);
          var displayName = subKey?.GetValue("DisplayName") as string;
          if (!string.IsNullOrWhiteSpace(displayName) && displayName.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase)) {
            var installLocation = subKey?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(installLocation)) {
              var candidate = FirstExisting(installLocation);
              if (candidate is not null) return candidate;
            }
          }
        }
      }
    }
    catch (UnauthorizedAccessException) {
    }
    catch (System.Security.SecurityException) {
    }
    catch (IOException) {
    }

    // 3) Not found
    return null;
  }

  // ldconsole.exe is the current name; dnconsole.exe is the legacy alias in older installs.
  private static string? FirstExisting(string dir) {
    foreach (var name in new[] { "ldconsole.exe", "dnconsole.exe" }) {
      var candidate = Path.Combine(dir, name);
      if (File.Exists(candidate)) return candidate;
    }
    return null;
  }
}
