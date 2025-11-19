using GameBot.Domain.Sessions;

namespace GameBot.Emulator.Session;

public interface ISessionManager
{
    int ActiveCount { get; }
    bool CanCreateSession { get; }
    EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null);
    EmulatorSession? GetSession(string id);
    IReadOnlyCollection<EmulatorSession> ListSessions();
    bool StopSession(string id);
    Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default);
    Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default);
}

public sealed record InputAction(string Type, Dictionary<string, object> Args, int? DelayMs = null, int? DurationMs = null);
