using System.Collections.Generic;
using System.Collections.ObjectModel;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;

namespace GameBot.Domain.Commands {
  public enum SequenceStepType {
    Command,
    Action,
    Conditional,
    /// <summary>Executes a loop (count, while, or repeat-until) over its <see cref="SequenceStep.Body"/>.</summary>
    Loop,
    /// <summary>Exits the enclosing loop immediately, optionally only when a condition is true.</summary>
    Break,
    /// <summary>Evaluates a condition once and executes the then branch (<see cref="SequenceStep.Body"/>) or the else branch (<see cref="SequenceStep.ElseBody"/>).</summary>
    If
  }

  public sealed class SequenceActionPayload {
    public string Type { get; set; } = string.Empty;
    public string? SchemaVersion { get; set; }
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public Dictionary<string, object?> Parameters { get; } = new();
  }

  public sealed class SequenceCommandReference {
    public string CommandId { get; set; } = string.Empty;
    public string? CommandName { get; set; }
  }

  /// <summary>
  /// Minimal step model; detailed validation and behaviors added in US1/US2/US3 phases.
  /// </summary>
  public class SequenceStep {
    public int Order { get; set; }
    public string StepId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string CommandId { get; set; } = string.Empty;
    public SequenceCommandReference? CommandReference { get; set; }
    public SequenceStepType StepType { get; set; } = SequenceStepType.Command;
    public SequenceActionPayload? Action { get; set; }
    public WaitForImageConfig? WaitForImage { get; set; }
    public SequenceStepCondition? Condition { get; set; }
    public ImageVisibleCondition? ConditionExpression { get; set; }
    public int? DelayMs { get; set; }
    public DelayRangeMs? DelayRangeMs { get; set; }
    public int? TimeoutMs { get; set; }
    public RetryPolicy? Retry { get; set; }
    public GateConfig? Gate { get; set; }

    /// <summary>
    /// Opt-in strictness for a command step: when <c>true</c>, the step FAILS (and so aborts the
    /// sequence) if the command it invoked dispatched no input to the device — typically an
    /// image-anchored tap whose template was not found within its retries.
    /// <para>
    /// Defaults to <c>false</c>, which keeps the long-standing behavior of carrying on, because
    /// "tap it if it happens to be there" is a deliberate and widespread authoring pattern: loops
    /// that drain a list until nothing matches, and retry loops that expect early misses, both
    /// rely on a missed tap being survivable. Turning that into a failure by default would abort
    /// those sequences on the very iteration that is supposed to end them.
    /// </para>
    /// <para>
    /// Set it on steps that guard a screen transition, where a missed tap means every later step
    /// is running against the wrong screen. Regardless of this flag, a step that dispatched
    /// nothing now reports <c>not_executed</c> rather than <c>executed</c>, so the miss is always
    /// visible in the execution log and available to a <c>commandOutcome</c> condition.
    /// </para>
    /// </summary>
    public bool RequireDispatch { get; set; }

    // Loop-step properties (StepType == Loop)
    /// <summary>Loop configuration (count, while, or repeat-until). Required when <see cref="StepType"/> is <see cref="SequenceStepType.Loop"/>.</summary>
    public LoopConfig? Loop { get; set; }
    /// <summary>
    /// Child steps executed on each loop iteration (StepType == Loop) or as the then branch
    /// (StepType == If). Empty list is valid (zero-body loop / no-op then branch).
    /// </summary>
    public IReadOnlyList<SequenceStep> Body { get; init; } = Array.Empty<SequenceStep>();

    // If-step properties (StepType == If)
    /// <summary>If configuration (branch condition). Required when <see cref="StepType"/> is <see cref="SequenceStepType.If"/>.</summary>
    public IfConfig? If { get; set; }
    /// <summary>
    /// Else-branch steps for if steps. <c>null</c> means the else branch is absent; an empty
    /// list means an else branch exists but has no steps. Both execute as a no-op.
    /// </summary>
    public IReadOnlyList<SequenceStep>? ElseBody { get; init; }

    // Break-step property (StepType == Break)
    /// <summary>Optional condition for a conditional break. When <c>null</c> the break is unconditional.</summary>
    public SequenceStepCondition? BreakCondition { get; set; }

    /// <summary>
    /// Values bound for the parameters of the command this step invokes (feature 078). Meaningful
    /// only when <see cref="StepType"/> is <see cref="SequenceStepType.Command"/>. A binding whose
    /// value is null means "inherit" — the default for every row — so a value already present in the
    /// enclosing scope flows down with no configuration.
    /// <para>
    /// Action-payload parameters need no equivalent member: <see cref="SequenceActionPayload.Parameters"/>
    /// already holds arbitrary values and accepts a placeholder string even in a numeric slot.
    /// </para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Collection<ParameterBinding>? ParameterBindings { get; init; }
  }

  public class DelayRangeMs {
    public int Min { get; set; }
    public int Max { get; set; }
  }

  public class RetryPolicy {
    public int MaxAttempts { get; set; }
    public int? BackoffMs { get; set; }
  }

  public enum GateCondition {
    Present,
    Absent
  }

  public class GateConfig {
    public string TargetId { get; set; } = string.Empty;
    public GateCondition Condition { get; set; } = GateCondition.Present;
    public double? Confidence { get; set; }
  }
}
