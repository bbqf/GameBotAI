using System.Collections.ObjectModel;

namespace GameBot.Domain.Commands;

public sealed class Command {
  public required string Id { get; set; }
  public required string Name { get; set; }
  public string? TriggerId { get; set; }
  public Collection<CommandStep> Steps { get; init; } = new();
  // Optional detection target enabling image-based coordinate resolution for action steps
  public DetectionTarget? Detection { get; set; }
}
