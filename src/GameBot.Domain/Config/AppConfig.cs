namespace GameBot.Domain.Config;

/// <summary>
/// Global application configuration settings for the GameBot domain layer.
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Global safety ceiling on the number of iterations a loop step may execute.
    /// Applied when no per-loop <see cref="Commands.LoopConfig.MaxIterations"/> override is
    /// set on the step.  Must be greater than zero.
    /// </summary>
    public int LoopMaxIterations { get; init; } = 1000;
}
