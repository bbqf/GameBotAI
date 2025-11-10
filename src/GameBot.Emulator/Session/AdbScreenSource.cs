using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using GameBot.Domain.Profiles.Evaluators;
using GameBot.Emulator.Adb;
using Microsoft.Extensions.Logging;

namespace GameBot.Emulator.Session;

/// <summary>
/// IScreenSource implementation backed by an active emulator session's ADB device screencap. Windows-only.
/// Returns null if no session/device or on failure.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AdbScreenSource : IScreenSource
{
    private readonly ISessionManager _sessions;
    private readonly ILogger<AdbScreenSource> _logger;
    private readonly ILogger<AdbClient> _adbLogger;

    public AdbScreenSource(ISessionManager sessions, ILogger<AdbScreenSource> logger, ILogger<AdbClient> adbLogger)
    {
        _sessions = sessions;
        _logger = logger;
        _adbLogger = adbLogger;
    }

    public Bitmap? GetLatestScreenshot()
    {
        try
        {
            // Prefer capturing directly via adb: list devices and capture from the first 'device' state
            var adb = new AdbClient(_adbLogger);
            var devs = adb.ExecAsync("devices -l").GetAwaiter().GetResult();
            if (devs.ExitCode != 0 || string.IsNullOrWhiteSpace(devs.StdOut)) return null;
            var lines = devs.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string? serial = null;
            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase)) { serial = parts[0]; break; }
            }
            if (string.IsNullOrWhiteSpace(serial)) return null;

            var png = new AdbClient(_adbLogger).WithSerial(serial).GetScreenshotPngAsync(default).GetAwaiter().GetResult();
            if (png is null || png.Length == 0) return null;
            using var ms = new MemoryStream(png);
            return new Bitmap(ms);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AdbScreenSource failed to obtain screenshot; returning null");
            return null;
        }
    }
}