namespace GameBot.Domain.Sessions;

public sealed class SessionCache
{
    public required string SessionId { get; init; }
    public required string GameId { get; init; }
    public required string EmulatorId { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime LastSeenAtUtc { get; init; }
    public SessionCacheStatus Status { get; init; } = SessionCacheStatus.Running;
    public string Source { get; init; } = "start-session";
}

public enum SessionCacheStatus
{
    Running,
    Stopping,
    Stopped,
    Stale
}
