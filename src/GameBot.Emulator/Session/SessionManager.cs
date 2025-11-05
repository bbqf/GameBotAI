using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using GameBot.Domain.Sessions;
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
        _options = options.Value;
        _logger = logger;
        _logger.LogInformation("SessionManager started with MaxConcurrent={Max}, IdleTimeout={Timeout}s", _options.MaxConcurrentSessions, _options.IdleTimeoutSeconds);
    }

    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => ActiveCount < _options.MaxConcurrentSessions;

    public EmulatorSession CreateSession(string gameIdOrPath, string? profileId = null)
    {
        CleanupIdleSessions();
        if (!CanCreateSession)
        {
            _logger.LogWarning("Capacity exceeded. Active={Active} Max={Max}", ActiveCount, _options.MaxConcurrentSessions);
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
        _logger.LogInformation("Session {Id} created for {Game}", id, gameIdOrPath);
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
            _logger.LogInformation("Session {Id} stopped", id);
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
        _logger.LogDebug("Session {Id} accepted {Count} input actions", id, count);
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
        _logger.LogDebug("Snapshot for {Id} generated in {ElapsedMs} ms", id, sw.ElapsedMilliseconds);
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
                    _logger.LogInformation("Session {Id} evicted due to idle timeout ({Timeout}s)", kv.Key, _options.IdleTimeoutSeconds);
                }
            }
        }
    }
}
