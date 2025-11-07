using System.Collections.ObjectModel;

namespace GameBot.Service.Models;

internal sealed class CreateSessionRequest
{
    public string? GameId { get; set; }
    public string? GamePath { get; set; }
    public string? ProfileId { get; set; }
    public string? AdbSerial { get; set; }
}

internal sealed class CreateSessionResponse
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required string GameId { get; init; }
}

internal sealed class InputActionsRequest
{
    public required Collection<InputActionDto> Actions { get; init; }
}

internal sealed class InputActionDto
{
    public required string Type { get; init; }
    public Dictionary<string, object> Args { get; init; } = new();
    public int? DelayMs { get; init; }
    public int? DurationMs { get; init; }
}
