using System.Collections.ObjectModel;

namespace GameBot.Service.Models;

internal sealed class CreateActionRequest
{
    public required string Name { get; set; }
    public required string GameId { get; set; }
    public Collection<InputActionDto> Steps { get; init; } = new();
    public Collection<string> Checkpoints { get; init; } = new();
}

internal sealed class ActionResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string GameId { get; init; }
    public Collection<InputActionDto> Steps { get; init; } = new();
    public Collection<string> Checkpoints { get; init; } = new();
}
