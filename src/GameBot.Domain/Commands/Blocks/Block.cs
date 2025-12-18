namespace GameBot.Domain.Commands.Blocks;

public abstract class Block
{
    public required string Type { get; init; } // repeatCount | repeatUntil | while | ifElse
    public System.Collections.ObjectModel.Collection<object> Steps { get; init; } = new(); // heterogeneous: Step | Block
}

public sealed class RepeatCountBlock : Block
{
    public int MaxIterations { get; init; }
    public int? CadenceMs { get; init; }
    public ControlConfig? Control { get; init; }

    public RepeatCountBlock()
    {
        Type = "repeatCount";
    }
}

public sealed class RepeatUntilBlock : Block
{
    public int? TimeoutMs { get; init; }
    public int? MaxIterations { get; init; }
    public int? CadenceMs { get; init; }
    public Condition? Condition { get; init; }
    public ControlConfig? Control { get; init; }

    public RepeatUntilBlock()
    {
        Type = "repeatUntil";
    }
}

public sealed class WhileBlock : Block
{
    public int? TimeoutMs { get; init; }
    public int? MaxIterations { get; init; }
    public int? CadenceMs { get; init; }
    public Condition? Condition { get; init; }
    public ControlConfig? Control { get; init; }

    public WhileBlock()
    {
        Type = "while";
    }
}

public sealed class IfElseBlock : Block
{
    public Condition? Condition { get; init; }
    public System.Collections.ObjectModel.Collection<object> ElseSteps { get; init; } = new();

    public IfElseBlock()
    {
        Type = "ifElse";
    }
}

public sealed class ControlConfig
{
    public Condition? BreakOn { get; init; }
    public Condition? ContinueOn { get; init; }
}