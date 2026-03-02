using System.Text.Json.Serialization;

namespace GameBot.Domain.Logging;

public sealed class ExecutionLogRetentionPolicy
{
  public bool Enabled { get; init; } = true;
  public int RetentionDays { get; init; } = 60;
  public int CleanupIntervalMinutes { get; init; } = 30;
  public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

  [JsonIgnore]
  public static ExecutionLogRetentionPolicy Default => new();
}
