namespace GameBot.Service.Contracts.Sequences;

// NOTE: DTO stubs for future explicit model binding if needed.
public abstract class BlockDto
{
    public string Type { get; set; } = string.Empty;
}

public sealed class RepeatCountBlockDto : BlockDto
{
    public int MaxIterations { get; set; }
    public int? CadenceMs { get; set; }
}

public sealed class RepeatUntilBlockDto : BlockDto
{
    public int? TimeoutMs { get; set; }
    public int? MaxIterations { get; set; }
    public int? CadenceMs { get; set; }
}

public sealed class WhileBlockDto : BlockDto
{
    public int? TimeoutMs { get; set; }
    public int? MaxIterations { get; set; }
    public int? CadenceMs { get; set; }
}

public sealed class IfElseBlockDto : BlockDto
{
    public object? Condition { get; set; }
}