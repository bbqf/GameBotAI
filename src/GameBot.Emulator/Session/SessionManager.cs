using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Adb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameBot.Emulator.Session;

public sealed class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, EmulatorSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;
    private readonly SessionOptions _options;
    private readonly bool _useAdb;
    private TimeSpan IdleTimeout => TimeSpan.FromSeconds(Math.Max(1, _options.IdleTimeoutSeconds));
    private static readonly char[] LineSplit = new[] { '\r', '\n' };

    public SessionManager(IOptions<SessionOptions> options, ILogger<SessionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
        var useAdbEnv = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
        _useAdb = OperatingSystem.IsWindows() && string.Equals(useAdbEnv, "true", StringComparison.OrdinalIgnoreCase);
        Log.SessionManagerStarted(_logger, _options.MaxConcurrentSessions, _options.IdleTimeoutSeconds);
    }

    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => ActiveCount < _options.MaxConcurrentSessions;

    public EmulatorSession CreateSession(string gameIdOrPath, string? profileId = null)
    {
        CleanupIdleSessions();
        if (!CanCreateSession)
        {
            Log.CapacityExceeded(_logger, ActiveCount, _options.MaxConcurrentSessions);
            throw new InvalidOperationException("capacity_exceeded");
        }
        var id = Guid.NewGuid().ToString("N");
        var sess = new EmulatorSession
        {
            Id = id,
            GameId = gameIdOrPath,
            Status = SessionStatus.Running,
            Health = SessionHealth.Ok,
            CapacitySlot = 0,
            LastActivity = DateTimeOffset.UtcNow
        };
        // Attempt to bind a device serial if ADB mode enabled
        if (_useAdb)
        {
            try
            {
                sess.DeviceSerial = ResolveDeviceSerial();
                if (sess.DeviceSerial is null)
                {
                    Log.AdbNoDevice(_logger, id);
                }
            }
            catch (InvalidOperationException ex)
            {
                Log.AdbResolveFailed(_logger, id, ex);
            }
        }
        _sessions[id] = sess;
        Log.SessionCreated(_logger, id, gameIdOrPath);
        return sess;
    }

    public EmulatorSession? GetSession(string id)
    {
        CleanupIdleSessions();
        if (_sessions.TryGetValue(id, out var s))
        {
            s.LastActivity = DateTimeOffset.UtcNow;
            return s;
        }
        return null;
    }

    public bool StopSession(string id)
    {
        if (_sessions.TryGetValue(id, out var s))
        {
            s.Status = SessionStatus.Stopping;
            _sessions.TryRemove(id, out _);
            Log.SessionStopped(_logger, id);
            return true;
        }
        return false;
    }

    public int SendInputs(string id, IEnumerable<InputAction> actions)
    {
        if (!_sessions.TryGetValue(id, out var s)) return 0;
        s.LastActivity = DateTimeOffset.UtcNow;
        var count = actions?.Count() ?? 0;

        // If ADB mode active and we have a device, try to execute inputs
        if (OperatingSystem.IsWindows() && _useAdb && !string.IsNullOrWhiteSpace(s.DeviceSerial))
        {
            var adb = new AdbClient().WithSerial(s.DeviceSerial);
            var executed = 0;
            foreach (var a in actions ?? Array.Empty<InputAction>())
            {
                try
                {
                    if (string.Equals(a.Type, "tap", StringComparison.OrdinalIgnoreCase))
                    {
                        var x = Convert.ToInt32(a.Args["x"], System.Globalization.CultureInfo.InvariantCulture);
                        var y = Convert.ToInt32(a.Args["y"], System.Globalization.CultureInfo.InvariantCulture);
                        var (code, _, _) = adb.TapAsync(x, y).GetAwaiter().GetResult();
                        if (code == 0) executed++;
                    }
                    else if (string.Equals(a.Type, "swipe", StringComparison.OrdinalIgnoreCase))
                    {
                        var x1 = Convert.ToInt32(a.Args["x1"], System.Globalization.CultureInfo.InvariantCulture);
                        var y1 = Convert.ToInt32(a.Args["y1"], System.Globalization.CultureInfo.InvariantCulture);
                        var x2 = Convert.ToInt32(a.Args["x2"], System.Globalization.CultureInfo.InvariantCulture);
                        var y2 = Convert.ToInt32(a.Args["y2"], System.Globalization.CultureInfo.InvariantCulture);
                        var duration = a.DurationMs;
                        var (code, _, _) = adb.SwipeAsync(x1, y1, x2, y2, duration).GetAwaiter().GetResult();
                        if (code == 0) executed++;
                    }
                    else if (string.Equals(a.Type, "key", StringComparison.OrdinalIgnoreCase))
                    {
                        var keyCode = Convert.ToInt32(a.Args["keyCode"], System.Globalization.CultureInfo.InvariantCulture);
                        var (code, _, _) = adb.KeyEventAsync(keyCode).GetAwaiter().GetResult();
                        if (code == 0) executed++;
                    }
                    // Optional future: handle delayMs by Thread.Sleep(a.DelayMs.Value)
                    if (a.DelayMs.HasValue && a.DelayMs.Value > 0)
                    {
                        // Blocking sleep is acceptable for MVP; future: queue or async pipeline
                        System.Threading.Thread.Sleep(a.DelayMs.Value);
                    }
                }
                catch (KeyNotFoundException ex)
                {
                    Log.AdbInputFailed(_logger, id, ex);
                }
                catch (FormatException ex)
                {
                    Log.AdbInputFailed(_logger, id, ex);
                }
                catch (InvalidCastException ex)
                {
                    Log.AdbInputFailed(_logger, id, ex);
                }
                catch (InvalidOperationException ex)
                {
                    Log.AdbInputFailed(_logger, id, ex);
                }
            }
            Log.InputsAccepted(_logger, id, executed);
            return executed;
        }

        Log.InputsAccepted(_logger, id, count);
        return count;
    }

    public async Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default)
    {
        CleanupIdleSessions();
        if (!_sessions.TryGetValue(id, out var s)) throw new KeyNotFoundException("Session not found");
        s.LastActivity = DateTimeOffset.UtcNow;
        // If ADB mode active and we have a device, try real screenshot
        if (OperatingSystem.IsWindows() && _useAdb && !string.IsNullOrWhiteSpace(s.DeviceSerial))
        {
            try
            {
                var adb = new AdbClient().WithSerial(s.DeviceSerial);
                var swReal = Stopwatch.StartNew();
                var png = await adb.GetScreenshotPngAsync(ct).ConfigureAwait(false);
                swReal.Stop();
                Log.SnapshotGenerated(_logger, id, swReal.ElapsedMilliseconds);
                return png;
            }
            catch (InvalidOperationException ex)
            {
                Log.AdbSnapshotFailed(_logger, id, ex);
            }
        }

        // Stub snapshot: Generate a 1x1 PNG
        var stub = GenerateStubPng(id);
        return await Task.FromResult(stub).ConfigureAwait(false);
    }

    private void CleanupIdleSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _sessions.ToArray())
        {
            var last = kv.Value.LastActivity;
            if (now - last > IdleTimeout)
            {
                if (_sessions.TryRemove(kv.Key, out _))
                {
                    Log.SessionEvicted(_logger, kv.Key, _options.IdleTimeoutSeconds);
                }
            }
        }
    }

    private string? ResolveDeviceSerial()
    {
        if (!_useAdb) return null;
        var preferred = Environment.GetEnvironmentVariable("GAMEBOT_ADB_SERIAL");
        if (!OperatingSystem.IsWindows()) return preferred;
        var adb = new AdbClient();
        var (code, stdout, _) = adb.ExecAsync("devices -l").GetAwaiter().GetResult();
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return preferred; // if adb fails, just return preferred (may be null)
        var serials = ParseDeviceSerials(stdout);
        if (!string.IsNullOrWhiteSpace(preferred) && serials.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            return preferred;
        return serials.FirstOrDefault();
    }

    private static List<string> ParseDeviceSerials(string stdout)
    {
        var list = new List<string>();
        var lines = stdout.Split(LineSplit, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var serial = parts[0];
            var state = parts[1];
            if (string.Equals(state, "device", StringComparison.OrdinalIgnoreCase))
                list.Add(serial);
        }
        return list;
    }

    private byte[] GenerateStubPng(string id)
    {
        using var bmp = new Bitmap(1, 1);
        bmp.SetPixel(0, 0, Color.Black);
        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        bmp.Save(ms, ImageFormat.Png);
        sw.Stop();
        Log.SnapshotGenerated(_logger, id, sw.ElapsedMilliseconds);
        return ms.ToArray();
    }
}

internal static class Log
{
    private static readonly Action<ILogger, int, int, Exception?> _sessionManagerStarted =
        LoggerMessage.Define<int, int>(LogLevel.Information, new EventId(1, nameof(SessionManagerStarted)),
            "SessionManager started with MaxConcurrent={Max}, IdleTimeout={Timeout}s");

    private static readonly Action<ILogger, int, int, Exception?> _capacityExceeded =
        LoggerMessage.Define<int, int>(LogLevel.Warning, new EventId(2, nameof(CapacityExceeded)),
            "Capacity exceeded. Active={Active} Max={Max}");

    private static readonly Action<ILogger, string, string, Exception?> _sessionCreated =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(3, nameof(SessionCreated)),
            "Session {Id} created for {Game}");

    private static readonly Action<ILogger, string, Exception?> _sessionStopped =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(4, nameof(SessionStopped)),
            "Session {Id} stopped");

    private static readonly Action<ILogger, string, int, Exception?> _inputsAccepted =
        LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(5, nameof(InputsAccepted)),
            "Session {Id} accepted {Count} input actions");

    private static readonly Action<ILogger, string, long, Exception?> _snapshotGenerated =
        LoggerMessage.Define<string, long>(LogLevel.Debug, new EventId(6, nameof(SnapshotGenerated)),
            "Snapshot for {Id} generated in {ElapsedMs} ms");

    private static readonly Action<ILogger, string, int, Exception?> _sessionEvicted =
        LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(7, nameof(SessionEvicted)),
            "Session {Id} evicted due to idle timeout ({Timeout}s)");

    private static readonly Action<ILogger, string, Exception?> _adbResolveFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(20, nameof(AdbResolveFailed)),
            "Failed to resolve ADB device; session {Id} will run in stub mode.");

    private static readonly Action<ILogger, string, Exception?> _adbInputFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(21, nameof(AdbInputFailed)),
            "Failed to execute input action for session {Id}");

    private static readonly Action<ILogger, string, Exception?> _adbSnapshotFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(22, nameof(AdbSnapshotFailed)),
            "ADB snapshot failed for {Id}; falling back to stub image.");

    private static readonly Action<ILogger, string, Exception?> _adbNoDeviceLog =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(23, nameof(AdbNoDevice)),
            "ADB mode requested but no 'device' found; session {Id} will run in stub mode.");

    public static void SessionManagerStarted(ILogger l, int max, int timeout) => _sessionManagerStarted(l, max, timeout, null);
    public static void CapacityExceeded(ILogger l, int active, int max) => _capacityExceeded(l, active, max, null);
    public static void SessionCreated(ILogger l, string id, string game) => _sessionCreated(l, id, game, null);
    public static void SessionStopped(ILogger l, string id) => _sessionStopped(l, id, null);
    public static void InputsAccepted(ILogger l, string id, int count) => _inputsAccepted(l, id, count, null);
    public static void SnapshotGenerated(ILogger l, string id, long elapsedMs) => _snapshotGenerated(l, id, elapsedMs, null);
    public static void SessionEvicted(ILogger l, string id, int timeout) => _sessionEvicted(l, id, timeout, null);
    public static void AdbResolveFailed(ILogger l, string id, Exception ex) => _adbResolveFailed(l, id, ex);
    public static void AdbInputFailed(ILogger l, string id, Exception ex) => _adbInputFailed(l, id, ex);
    public static void AdbSnapshotFailed(ILogger l, string id, Exception ex) => _adbSnapshotFailed(l, id, ex);
    public static void AdbNoDevice(ILogger l, string id) => _adbNoDeviceLog(l, id, null);
}
