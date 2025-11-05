using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GameBot.Emulator.Adb;

public static class AdbResolver
{
    // Returns the path to adb.exe using LDPlayer-first strategy, else returns null to use PATH
    [SupportedOSPlatform("windows")]
    public static string? ResolveAdbPath()
    {
        // 1) Config override via env var
        var envOverride = Environment.GetEnvironmentVariable("GAMEBOT_ADB_PATH");
        if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
            return envOverride;

        // 2) LDPlayer env hints
        var ldpHome = Environment.GetEnvironmentVariable("LDPLAYER_HOME") ?? Environment.GetEnvironmentVariable("LDP_HOME");
        if (!string.IsNullOrWhiteSpace(ldpHome))
        {
            var candidate = Path.Combine(ldpHome, "adb.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // 2b) Common install paths
        var probes = new[]
        {
            @"C:\\Program Files\\LDPlayer\\LDPlayer9\\adb.exe",
            @"C:\\Program Files\\LDPlayer\\LDPlayer8\\adb.exe",
            @"C:\\Program Files\\LDPlayer\\adb.exe",
            @"C:\\LDPlayer\\adb.exe"
        };
        foreach (var p in probes) if (File.Exists(p)) return p;

        // 2c) Registry uninstall entries
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
            if (key != null)
            {
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(sub);
                    var displayName = subKey?.GetValue("DisplayName") as string;
                    if (!string.IsNullOrWhiteSpace(displayName) && displayName.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase))
                    {
                        var installLocation = subKey?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(installLocation))
                        {
                            var candidate = Path.Combine(installLocation, "adb.exe");
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // ignore registry access errors
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (IOException)
        {
        }

        // 3) Fallback: null => use PATH
        return null;
    }
}
