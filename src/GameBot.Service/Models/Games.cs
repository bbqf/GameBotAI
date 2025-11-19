namespace GameBot.Service.Models;

internal sealed class CreateGameRequest {
  public required string Name { get; set; }
  public string? Description { get; set; }
}

internal sealed class GameResponse {
  public required string Id { get; init; }
  public required string Name { get; init; }
  public string? Description { get; init; }
}
