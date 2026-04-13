using System.Text.Json.Serialization;

namespace GameBot.Domain.Commands;

/// <summary>
/// Base configuration for a loop step. The concrete subtype is selected by the
/// <c>loopType</c> JSON discriminator (<c>count</c>, <c>while</c>, or <c>repeatUntil</c>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "loopType")]
[JsonDerivedType(typeof(CountLoopConfig), typeDiscriminator: "count")]
[JsonDerivedType(typeof(WhileLoopConfig), typeDiscriminator: "while")]
[JsonDerivedType(typeof(RepeatUntilLoopConfig), typeDiscriminator: "repeatUntil")]
public abstract class LoopConfig
{
    /// <summary>
    /// Optional per-loop safety ceiling on the number of iterations.
    /// When set, overrides the global <see cref="Config.AppConfig.LoopMaxIterations"/>.
    /// Must be greater than zero when present.
    /// </summary>
    public int? MaxIterations { get; set; }
}

/// <summary>
/// Executes the loop body a fixed number of times equal to <see cref="Count"/>.
/// A <see cref="Count"/> of zero skips the body entirely and is treated as success.
/// </summary>
public sealed class CountLoopConfig : LoopConfig
{
    /// <summary>
    /// Number of iterations to execute. Must be zero or greater.
    /// </summary>
    public int Count { get; set; }
}

/// <summary>
/// Executes the loop body while <see cref="Condition"/> evaluates to <c>true</c>,
/// re-checking the condition before each iteration.  When the condition is <c>false</c>
/// on entry the body is skipped and the step succeeds.
/// </summary>
public sealed class WhileLoopConfig : LoopConfig
{
    /// <summary>
    /// Condition evaluated before each iteration. Loop exits when the condition is false.
    /// </summary>
    public required SequenceStepCondition Condition { get; set; }
}

/// <summary>
/// Executes the loop body at least once, then evaluates <see cref="Condition"/> after each
/// iteration; the loop exits when the condition becomes <c>true</c>.
/// </summary>
public sealed class RepeatUntilLoopConfig : LoopConfig
{
    /// <summary>
    /// Exit condition evaluated after each iteration. Loop exits when the condition is true.
    /// </summary>
    public required SequenceStepCondition Condition { get; set; }
}
