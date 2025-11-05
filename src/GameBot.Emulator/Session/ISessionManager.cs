using GameBot.Domain.Sessions;

namespace GameBot.Emulator.Session;

public interface ISessionManager
{
    EmulatorSession CreateSession(string gameIdOrPath, string? profileId = null);
    EmulatorSession? GetSession(string id);
    bool StopSession(string id);
    int SendInputs(string id, IEnumerable<InputAction> actions);
    Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default);
}

public sealed record InputAction(string Type, Dictionary<string, object> Args, int? DelayMs = null, int? DurationMs = null);
