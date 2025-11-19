using System.Collections.ObjectModel;
using GameBot.Domain.Actions;

namespace GameBot.Domain.Actions;

public sealed class Action {
  public required string Id { get; set; }
  public required string Name { get; set; }
  public required string GameId { get; set; }
  public Collection<InputAction> Steps { get; init; } = new();
  public Collection<string> Checkpoints { get; init; } = new();
}
