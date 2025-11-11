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
            // Session-aware: prefer an active session with a bound device serial
            var sess = _sessions.ListSessions()
                                 .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.DeviceSerial) && s.Status == GameBot.Domain.Sessions.SessionStatus.Running);
            if (sess is null)
            {
                return null; // No active session with device; skip capture
            }
            var serial = sess.DeviceSerial!;
            var png = new AdbClient(_adbLogger).WithSerial(serial).GetScreenshotPngAsync(default).GetAwaiter().GetResult();
            if (png is null || png.Length == 0) return null;
            using var ms = new MemoryStream(png);
            return new Bitmap(ms);
        }
        catch (Exception ex)
        {
            AdbScreenSourceLog.CaptureFail(_logger, ex);
            return null;
        }
    }
}

internal static partial class AdbScreenSourceLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Debug, Message = "AdbScreenSource failed to obtain screenshot; returning null")]
    public static partial void CaptureFail(ILogger logger, Exception ex);
}