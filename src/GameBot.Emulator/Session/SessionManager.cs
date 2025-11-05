using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using GameBot.Domain.Sessions;

namespace GameBot.Emulator.Session;

public sealed class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, EmulatorSession> _sessions = new();

    public EmulatorSession CreateSession(string gameIdOrPath, string? profileId = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var sess = new EmulatorSession
        {
            Id = id,
            GameId = gameIdOrPath,
            Status = SessionStatus.Running,
            Health = SessionHealth.Ok,
            CapacitySlot = 0
        };
        _sessions[id] = sess;
        return sess;
    }

    public EmulatorSession? GetSession(string id)
        => _sessions.TryGetValue(id, out var s) ? s : null;

    public bool StopSession(string id)
    {
        if (_sessions.TryGetValue(id, out var s))
        {
            s.Status = SessionStatus.Stopping;
            _sessions.TryRemove(id, out _);
            return true;
        }
        return false;
    }

    public int SendInputs(string id, IEnumerable<InputAction> actions)
    {
        // Stub: accept inputs without executing real ADB for now
        if (!_sessions.ContainsKey(id)) return 0;
        return actions?.Count() ?? 0;
    }

    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default)
    {
        // Stub snapshot: Generate a 1x1 PNG to satisfy contract and UI wiring
        if (!_sessions.ContainsKey(id)) throw new KeyNotFoundException("Session not found");
        using var bmp = new Bitmap(1, 1);
        bmp.SetPixel(0, 0, Color.Black);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return Task.FromResult(ms.ToArray());
    }
}
