namespace GameBot.Service.Models;

internal sealed class CreateGameRequest
{
    public required string Title { get; set; }
    public required string Path { get; set; }
    public required string Hash { get; set; }
    public string? Region { get; set; }
    public string? Notes { get; set; }
    public bool ComplianceAttestation { get; set; }
}

internal sealed class GameResponse
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Hash { get; init; }
    public required string Path { get; init; }
    public string? Region { get; init; }
    public string? Notes { get; init; }
    public bool ComplianceAttestation { get; init; }
}
