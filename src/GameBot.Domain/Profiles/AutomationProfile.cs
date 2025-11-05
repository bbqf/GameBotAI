using System.Collections.ObjectModel;
using GameBot.Domain.Profiles;

namespace GameBot.Domain.Profiles;

public sealed class AutomationProfile
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string GameId { get; set; }
    public Collection<InputAction> Steps { get; init; } = new();
    public Collection<string> Checkpoints { get; init; } = new();
}
