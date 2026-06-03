namespace GameBot.Domain.Games;

public sealed class GameArtifact {
  public required string Id { get; set; }
  public required string Name { get; set; }
  public string? Description { get; set; }
  /// <summary>Android package identifier used to detect and launch the game on an emulator.</summary>
  public string? PackageName { get; set; }
}
