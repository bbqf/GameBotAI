namespace GameBot.Service.Models;

public sealed class CreateSessionRequest
{
    public string? GameId { get; set; }
    public string? GamePath { get; set; }
    public string? ProfileId { get; set; }
}

public sealed class CreateSessionResponse
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required string GameId { get; init; }
}

public sealed class InputActionsRequest
{
    public required List<InputActionDto> Actions { get; init; }
}

public sealed class InputActionDto
{
    public required string Type { get; init; }
    public Dictionary<string, object> Args { get; init; } = new();
    public int? DelayMs { get; init; }
    public int? DurationMs { get; init; }
}
