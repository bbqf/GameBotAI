namespace GameBot.Service.Contracts.Sequences;

// NOTE: DTO stubs for future explicit model binding if needed.
internal abstract class BlockDto
{
    public string Type { get; set; } = string.Empty;
}

internal sealed class RepeatCountBlockDto : BlockDto
{
    public int MaxIterations { get; set; }
    public int? CadenceMs { get; set; }
}

internal sealed class RepeatUntilBlockDto : BlockDto
{
    public int? TimeoutMs { get; set; }
    public int? MaxIterations { get; set; }
    public int? CadenceMs { get; set; }
}

internal sealed class WhileBlockDto : BlockDto
{
    public int? TimeoutMs { get; set; }
    public int? MaxIterations { get; set; }
    public int? CadenceMs { get; set; }
}

internal sealed class IfElseBlockDto : BlockDto
{
    public object? Condition { get; set; }
}