using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using GameBot.Domain.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameBot.Emulator.Session;

public sealed class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, EmulatorSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;
    private readonly SessionOptions _options;
    private TimeSpan IdleTimeout => TimeSpan.FromSeconds(Math.Max(1, _options.IdleTimeoutSeconds));

    public SessionManager(IOptions<SessionOptions> options, ILogger<SessionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
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
        // Stub: accept inputs without executing real ADB for now
        if (!_sessions.TryGetValue(id, out var s)) return 0;
        s.LastActivity = DateTimeOffset.UtcNow;
        var count = actions?.Count() ?? 0;
        Log.InputsAccepted(_logger, id, count);
        return count;
    }

    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default)
    {
        CleanupIdleSessions();
        // Stub snapshot: Generate a 1x1 PNG to satisfy contract and UI wiring
        if (!_sessions.TryGetValue(id, out var s)) throw new KeyNotFoundException("Session not found");
        s.LastActivity = DateTimeOffset.UtcNow;
        using var bmp = new Bitmap(1, 1);
        bmp.SetPixel(0, 0, Color.Black);
        using var ms = new MemoryStream();
        var sw = Stopwatch.StartNew();
        bmp.Save(ms, ImageFormat.Png);
        sw.Stop();
        Log.SnapshotGenerated(_logger, id, sw.ElapsedMilliseconds);
        return Task.FromResult(ms.ToArray());
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

    public static void SessionManagerStarted(ILogger l, int max, int timeout) => _sessionManagerStarted(l, max, timeout, null);
    public static void CapacityExceeded(ILogger l, int active, int max) => _capacityExceeded(l, active, max, null);
    public static void SessionCreated(ILogger l, string id, string game) => _sessionCreated(l, id, game, null);
    public static void SessionStopped(ILogger l, string id) => _sessionStopped(l, id, null);
    public static void InputsAccepted(ILogger l, string id, int count) => _inputsAccepted(l, id, count, null);
    public static void SnapshotGenerated(ILogger l, string id, long elapsedMs) => _snapshotGenerated(l, id, elapsedMs, null);
    public static void SessionEvicted(ILogger l, string id, int timeout) => _sessionEvicted(l, id, timeout, null);
}
