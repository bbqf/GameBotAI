using System.Collections.ObjectModel;

namespace GameBot.Service.Models;

internal sealed class CreateCommandRequest
{
    public required string Name { get; set; }
    public string? TriggerId { get; set; }
    public Collection<CommandStepDto> Steps { get; init; } = new();
}

internal sealed class UpdateCommandRequest
{
    public string? Name { get; set; }
    public string? TriggerId { get; set; }
    public Collection<CommandStepDto>? Steps { get; init; }
}

internal sealed class CommandResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? TriggerId { get; init; }
    public Collection<CommandStepDto> Steps { get; init; } = new();
}

internal enum CommandStepTypeDto
{
    Action,
    Command
}

internal sealed class CommandStepDto
{
    public required CommandStepTypeDto Type { get; init; }
    public required string TargetId { get; init; }
    public int Order { get; init; }
}
