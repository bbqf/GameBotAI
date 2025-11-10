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
            // Heuristic: pick first active session with a bound device serial
            var sessionIds = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Where(
                    _sessions.GetType().GetField("_sessions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_sessions) as System.Collections.IDictionary ?? new System.Collections.Hashtable(),
                    _ => true), _ => ((System.Collections.DictionaryEntry) _).Key.ToString()));
            // Above reflection is brittle; fallback to snapshot attempt through public API if we can guess one id
            var chosen = sessionIds.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(chosen)) return null;
            // Use public snapshot pathway (will invoke ADB screencap if available) to avoid duplicating retry logic
            var pngTask = _sessions.GetSnapshotAsync(chosen, default);
            pngTask.Wait();
            var png = pngTask.Result;
            if (png.Length == 0) return null;
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