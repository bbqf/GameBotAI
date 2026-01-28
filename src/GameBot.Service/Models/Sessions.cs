// Suppress CA1515: DTOs are public for controller model binding
#pragma warning disable CA1515
using System.Collections.ObjectModel;
using GameBot.Domain.Sessions;

namespace GameBot.Service.Models;

public sealed class CreateSessionRequest {
  public string? GameId { get; set; }
  public string? GamePath { get; set; }
  public string? AdbSerial { get; set; }
}

public sealed class CreateSessionResponse {
  public required string Id { get; init; }
  public required string SessionId { get; init; }
  public required string Status { get; init; }
  public required string GameId { get; init; }
}

public sealed class InputActionsRequest {
  public required Collection<InputActionDto> Actions { get; init; }
}

public sealed class InputActionDto {
  public required string Type { get; init; }
  public Dictionary<string, object> Args { get; init; } = new();
  public int? DelayMs { get; init; }
  public int? DurationMs { get; init; }
}

public sealed class RunningSessionsResponse {
  public required IReadOnlyCollection<RunningSessionDto> Sessions { get; init; }
}

public sealed class RunningSessionDto {
  public required string SessionId { get; init; }
  public required string GameId { get; init; }
  public required string EmulatorId { get; init; }
  public DateTime StartedAtUtc { get; init; }
  public DateTime LastHeartbeatUtc { get; init; }
  public RunningSessionStatus Status { get; init; }
}

public sealed class StartSessionRequest {
  public required string GameId { get; init; }
  public required string EmulatorId { get; init; }
  public Dictionary<string, object>? Options { get; init; }
}

public sealed class StartSessionResponse {
  public required string SessionId { get; init; }
  public required IReadOnlyCollection<RunningSessionDto> RunningSessions { get; init; }
}

public sealed class StopSessionRequest {
  public required string SessionId { get; init; }
}
#pragma warning restore CA1515
