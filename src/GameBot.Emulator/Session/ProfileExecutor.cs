using GameBot.Domain.Profiles;

namespace GameBot.Emulator.Session;

public sealed class ProfileExecutor : IProfileExecutor
{
    private readonly IProfileRepository _profiles;
    private readonly ISessionManager _sessions;

    public ProfileExecutor(IProfileRepository profiles, ISessionManager sessions)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(sessions);
        _profiles = profiles;
        _sessions = sessions;
    }

    public async Task<int> ExecuteAsync(string sessionId, string profileId, CancellationToken ct = default)
    {
        // Validate session exists
        var session = _sessions.GetSession(sessionId);
        if (session is null)
            throw new KeyNotFoundException("Session not found");

        // Load profile
        var profile = await _profiles.GetAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null)
            throw new KeyNotFoundException("Profile not found");

        if (profile.Steps.Count == 0)
            return 0;

        // Map profile actions to session input actions
        var actions = profile.Steps.Select(a => new InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs)).ToList();

        // Simple MVP: send all inputs at once. Future: honor per-action delays sequentially.
        var accepted = await _sessions.SendInputsAsync(sessionId, actions, ct).ConfigureAwait(false);
        return accepted;
    }
}
