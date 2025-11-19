namespace GameBot.Domain.Games;

// Simplified game model: only id, name, description
public sealed class GameArtifact {
  public required string Id { get; set; }
  public required string Name { get; set; }
  public string? Description { get; set; }
}
