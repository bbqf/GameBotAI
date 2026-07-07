using System.Collections.Generic;
using GameBot.Domain.Commands;

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
