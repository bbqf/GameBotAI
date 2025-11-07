namespace GameBot.Domain.Sessions;

public enum SessionStatus { Creating, Running, Stopping, Stopped, Failed }
public enum SessionHealth { Ok, Degraded, Error }

public sealed class EmulatorSession
{
    public required string Id { get; init; }
    public required string GameId { get; init; }
    public SessionStatus Status { get; set; } = SessionStatus.Creating;
    public DateTimeOffset StartTime { get; set; } = DateTimeOffset.UtcNow;
    public TimeSpan Uptime => DateTimeOffset.UtcNow - StartTime;
    public SessionHealth Health { get; set; } = SessionHealth.Ok;
    public int CapacitySlot { get; set; }
    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
    public string? DeviceSerial { get; set; }
}
