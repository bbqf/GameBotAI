namespace GameBot.Domain.Games;

public sealed class GameArtifact
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Hash { get; set; }
    public required string Path { get; set; }
    public string? Region { get; set; }
    public string? Notes { get; set; }
    public bool ComplianceAttestation { get; set; }
}
