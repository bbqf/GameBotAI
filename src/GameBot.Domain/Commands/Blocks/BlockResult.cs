namespace GameBot.Domain.Commands.Blocks;

public sealed class BlockResult
{
    public string BlockType { get; init; } = string.Empty;
    public int Iterations { get; set; }
    public int Evaluations { get; set; }
    public string? BranchTaken { get; set; }
    public int DurationMs { get; set; }
    public int AppliedDelayMs { get; set; }
    public string Status { get; set; } = "Succeeded"; // Succeeded | Failed | Skipped
}