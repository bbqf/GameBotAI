namespace GameBot.Domain.Sessions;

public sealed class RunningSession
{
    public required string SessionId { get; init; }
    public required string GameId { get; init; }
    public required string EmulatorId { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime LastHeartbeatUtc { get; init; }
    public RunningSessionStatus Status { get; init; } = RunningSessionStatus.Running;
}

public enum RunningSessionStatus
{
    Running,
    Stopping
}
