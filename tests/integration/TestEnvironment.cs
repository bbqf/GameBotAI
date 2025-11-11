using System;
using System.IO;

namespace GameBot.IntegrationTests;

internal static class TestEnvironment
{
    // Creates a fresh, empty data directory and points the service to it.
    public static string PrepareCleanDataDir()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "GameBotTests");
        var dir = Path.Combine(baseDir, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", dir);
        return dir;
    }
}
