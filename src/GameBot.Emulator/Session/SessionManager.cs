using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Adb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameBot.Emulator.Session;

public sealed class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, EmulatorSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;
    private readonly ILogger<AdbClient> _adbLogger;
    private readonly SessionOptions _options;
    private readonly bool _useAdb;
    private readonly int _adbRetries;
    private readonly int _adbRetryDelayMs;
    private TimeSpan IdleTimeout => TimeSpan.FromSeconds(Math.Max(1, _options.IdleTimeoutSeconds));
    private static readonly char[] LineSplit = new[] { '\r', '\n' };

    public SessionManager(IOptions<SessionOptions> options, ILogger<SessionManager> logger, ILogger<AdbClient> adbLogger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(adbLogger);
        _options = options.Value;
        _logger = logger;
        _adbLogger = adbLogger;
        // Always attempt ADB on Windows by default; allow disabling (tests/CI) with GAMEBOT_USE_ADB=false
        var useAdbEnv = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
        _useAdb = OperatingSystem.IsWindows() && !string.Equals(useAdbEnv, "false", StringComparison.OrdinalIgnoreCase);
        _adbRetries = Math.Max(0, int.TryParse(Environment.GetEnvironmentVariable("GAMEBOT_ADB_RETRIES"), out var r) ? r : 2);
        _adbRetryDelayMs = Math.Max(0, int.TryParse(Environment.GetEnvironmentVariable("GAMEBOT_ADB_RETRY_DELAY_MS"), out var d) ? d : 100);
        Log.SessionManagerStarted(_logger, _options.MaxConcurrentSessions, _options.IdleTimeoutSeconds);
    }

    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => ActiveCount < _options.MaxConcurrentSessions;

    public EmulatorSession CreateSession(string gameIdOrPath, string? profileId = null, string? preferredDeviceSerial = null)
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
        // Attempt to bind a device serial if ADB enabled
        if (_useAdb)
        {
            try
            {
                sess.DeviceSerial = ResolveOrValidateDeviceSerial(preferredDeviceSerial);
                if (sess.DeviceSerial is null)
                {
                    Log.AdbNoDevice(_logger, id);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message == "no_adb_devices")
            {
                // propagate for API to turn into 404
                throw;
            }
            catch (KeyNotFoundException)
            {
                // propagate for API to turn into 404
                throw;
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

    public async Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(id, out var s)) return 0;
        s.LastActivity = DateTimeOffset.UtcNow;
        var count = actions?.Count() ?? 0;

        // If ADB enabled and we have a bound device, execute via ADB
        if (_useAdb && OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(s.DeviceSerial))
        {
            var adb = new AdbClient(_adbLogger).WithSerial(s.DeviceSerial);
            var executed = 0;
            foreach (var a in actions ?? Array.Empty<InputAction>())
            {
                try
                {
                    if (string.Equals(a.Type, "tap", StringComparison.OrdinalIgnoreCase))
                    {
                        var x = GetInt(a.Args, "x");
                        var y = GetInt(a.Args, "y");
                        bool ok;
                        if (_useAdb && OperatingSystem.IsWindows())
                        {
                            ok = false;
                            for (var attempt = 0; ; attempt++)
                            {
                                var (code, _, _) = await adb.TapAsync(x, y, ct).ConfigureAwait(false);
                                if (code == 0) { ok = true; break; }
                                if (attempt >= _adbRetries || ct.IsCancellationRequested) break;
                                Log.AdbRetry(_logger, id, "tap", attempt + 1);
                                if (_adbRetryDelayMs > 0) await Task.Delay(_adbRetryDelayMs, ct).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            ok = true; // stub success in non-Windows or non-ADB mode
                        }
                        if (ok) executed++;
                    }
                    else if (string.Equals(a.Type, "swipe", StringComparison.OrdinalIgnoreCase))
                    {
                        var x1 = GetInt(a.Args, "x1");
                        var y1 = GetInt(a.Args, "y1");
                        var x2 = GetInt(a.Args, "x2");
                        var y2 = GetInt(a.Args, "y2");
                        var duration = a.DurationMs;
                        bool ok;
                        if (_useAdb && OperatingSystem.IsWindows())
                        {
                            ok = false;
                            for (var attempt = 0; ; attempt++)
                            {
                                var (code, _, _) = await adb.SwipeAsync(x1, y1, x2, y2, duration, ct).ConfigureAwait(false);
                                if (code == 0) { ok = true; break; }
                                if (attempt >= _adbRetries || ct.IsCancellationRequested) break;
                                Log.AdbRetry(_logger, id, "swipe", attempt + 1);
                                if (_adbRetryDelayMs > 0) await Task.Delay(_adbRetryDelayMs, ct).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            ok = true;
                        }
                        if (ok) executed++;
                    }
                    else if (string.Equals(a.Type, "key", StringComparison.OrdinalIgnoreCase))
                    {
                        // Support either keyCode (int) or key (string symbolic name)
                        int keyCode;
                        if (a.Args.ContainsKey("keyCode"))
                        {
                            keyCode = GetInt(a.Args, "keyCode");
                        }
                        else if (a.Args.TryGetValue("key", out var keyRaw))
                        {
                            var keyName = keyRaw is JsonElement je && je.ValueKind == JsonValueKind.String ? je.GetString() : keyRaw?.ToString();
                            keyCode = ResolveAndroidKeyCode(keyName);
                        }
                        else
                        {
                            throw new KeyNotFoundException("keyCode or key is required for key action");
                        }
                        bool ok;
                        if (_useAdb && OperatingSystem.IsWindows())
                        {
                            ok = false;
                            for (var attempt = 0; ; attempt++)
                            {
                                var (code, _, _) = await adb.KeyEventAsync(keyCode, ct).ConfigureAwait(false);
                                if (code == 0) { ok = true; break; }
                                if (attempt >= _adbRetries || ct.IsCancellationRequested) break;
                                Log.AdbRetry(_logger, id, "key", attempt + 1);
                                if (_adbRetryDelayMs > 0) await Task.Delay(_adbRetryDelayMs, ct).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            ok = true;
                        }
                        if (ok) executed++;
                    }
                    // Per-action delay handling (cancellable)
                    if (a.DelayMs.HasValue && a.DelayMs.Value > 0)
                    {
                        await Task.Delay(a.DelayMs.Value, ct).ConfigureAwait(false);
                    }
                    if (ct.IsCancellationRequested) break;
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
        // Still honor delay pacing in stub mode (optional; only if delays exist)
        if (actions is not null)
        {
            foreach (var a in actions)
            {
                if (a.DelayMs.HasValue && a.DelayMs.Value > 0)
                {
                    await Task.Delay(a.DelayMs.Value, ct).ConfigureAwait(false);
                }
            }
        }
        return count;
    }

    public async Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default)
    {
        CleanupIdleSessions();
        if (!_sessions.TryGetValue(id, out var s)) throw new KeyNotFoundException("Session not found");
        s.LastActivity = DateTimeOffset.UtcNow;
        // If ADB enabled and we have a device bound, try real screenshot
        if (_useAdb && OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(s.DeviceSerial))
        {
            try
            {
                var adb = new AdbClient(_adbLogger).WithSerial(s.DeviceSerial);
                var swReal = Stopwatch.StartNew();
                byte[] png;
                var attempt = 0;
                while (true)
                {
                    try
                    {
                        png = await adb.GetScreenshotPngAsync(ct).ConfigureAwait(false);
                        break;
                    }
                    catch (InvalidOperationException) when (attempt < _adbRetries && !ct.IsCancellationRequested)
                    {
                        attempt++;
                        Log.AdbRetry(_logger, id, "screencap", attempt);
                        if (_adbRetryDelayMs > 0) await Task.Delay(_adbRetryDelayMs, ct).ConfigureAwait(false);
                        continue;
                    }
                }
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

    private string? ResolveOrValidateDeviceSerial(string? preferred)
    {
        if (!_useAdb) return null;
        if (!OperatingSystem.IsWindows()) return preferred; // platform not supported, keep provided value if any
    var adb = new AdbClient(_adbLogger);
        var (code, stdout, _) = adb.ExecAsync("devices -l").GetAwaiter().GetResult();
        var serials = code == 0 && !string.IsNullOrWhiteSpace(stdout) ? ParseDeviceSerials(stdout) : new List<string>();
        if (serials.Count == 0)
        {
            throw new InvalidOperationException("no_adb_devices");
        }
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            if (serials.Contains(preferred, StringComparer.OrdinalIgnoreCase)) return preferred;
            throw new KeyNotFoundException($"ADB device '{preferred}' not found");
        }
        return serials.First();
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

    // Note: ADB retries are implemented inline where Windows-only calls are made to satisfy platform analyzers.
    private static int GetInt(Dictionary<string, object> args, string key)
    {
        if (!args.TryGetValue(key, out var raw)) throw new KeyNotFoundException(key);
        switch (raw)
        {
            case int i:
                return i;
            case long l:
                checked { return (int)l; }
            case double d:
                return (int)d;
            case float f:
                return (int)f;
            case decimal m:
                return (int)m;
            case string s:
                return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
            case JsonElement je:
                if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var num)) return num;
                if (je.ValueKind == JsonValueKind.String)
                {
                    var str = je.GetString();
                    if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
                }
                throw new FormatException($"Argument '{key}' is not a valid integer JSON value (kind={je.ValueKind}).");
            default:
                throw new InvalidCastException($"Argument '{key}' of type '{raw.GetType().FullName}' cannot be converted to int.");
        }
    }

    // Minimal Android key name mapping for common special keys
    private static readonly Dictionary<string, int> KeyNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HOME"] = 3,
        ["BACK"] = 4,
        ["ESCAPE"] = 111, // KEYCODE_ESCAPE
        ["MENU"] = 82,
        ["ENTER"] = 66,
        ["SPACE"] = 62,
        ["TAB"] = 61,
        ["DEL"] = 67,
        ["DELETE"] = 67,
        ["UP"] = 19,
        ["DOWN"] = 20,
        ["LEFT"] = 21,
        ["RIGHT"] = 22,
        ["VOLUME_UP"] = 24,
        ["VOLUME_DOWN"] = 25,
        ["POWER"] = 26,
        ["A"] = 29,
        ["B"] = 30,
        ["C"] = 31,
        ["D"] = 32,
        ["E"] = 33,
        ["F"] = 34,
        ["G"] = 35,
        ["H"] = 36,
        ["I"] = 37,
        ["J"] = 38,
        ["K"] = 39,
        ["L"] = 40,
        ["M"] = 41,
        ["N"] = 42,
        ["O"] = 43,
        ["P"] = 44,
        ["Q"] = 45,
        ["R"] = 46,
        ["S"] = 47,
        ["T"] = 48,
        ["U"] = 49,
        ["V"] = 50,
        ["W"] = 51,
        ["X"] = 52,
        ["Y"] = 53,
        ["Z"] = 54
    };

    private static int ResolveAndroidKeyCode(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new FormatException("Empty key name");
        if (KeyNameMap.TryGetValue(name.Trim(), out var code)) return code;
        throw new KeyNotFoundException($"Unknown key name '{name}'");
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

    private static readonly Action<ILogger, string, string, int, Exception?> _adbRetry =
        LoggerMessage.Define<string, string, int>(LogLevel.Debug, new EventId(24, nameof(AdbRetry)),
            "Session {Id}: retrying ADB {Operation}, attempt {Attempt}");

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
    public static void AdbRetry(ILogger l, string id, string operation, int attempt) => _adbRetry(l, id, operation, attempt, null);
}
