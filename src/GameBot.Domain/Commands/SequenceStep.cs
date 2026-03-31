using System.Collections.Generic;

namespace GameBot.Domain.Commands
{
    public enum SequenceStepType
    {
        Command,
        Action,
        Conditional,
        /// <summary>Executes a loop (count, while, or repeat-until) over its <see cref="SequenceStep.Body"/>.</summary>
        Loop,
        /// <summary>Exits the enclosing loop immediately, optionally only when a condition is true.</summary>
        Break
    }

    public sealed class SequenceActionPayload
    {
        public string Type { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
        public Dictionary<string, object?> Parameters { get; } = new();
    }

    /// <summary>
    /// Minimal step model; detailed validation and behaviors added in US1/US2/US3 phases.
    /// </summary>
    public class SequenceStep
    {
        public int Order { get; set; }
        public string StepId { get; set; } = string.Empty;
        public string? Label { get; set; }
        public string CommandId { get; set; } = string.Empty;
        public SequenceStepType StepType { get; set; } = SequenceStepType.Command;
        public SequenceActionPayload? Action { get; set; }
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
        /// <summary>Child steps executed on each loop iteration. Empty list is valid (zero-body loop).</summary>
        public IReadOnlyList<SequenceStep> Body { get; init; } = Array.Empty<SequenceStep>();

        // Break-step property (StepType == Break)
        /// <summary>Optional condition for a conditional break. When <c>null</c> the break is unconditional.</summary>
        public SequenceStepCondition? BreakCondition { get; set; }
    }

    public class DelayRangeMs
    {
        public int Min { get; set; }
        public int Max { get; set; }
    }

    public class RetryPolicy
    {
        public int MaxAttempts { get; set; }
        public int? BackoffMs { get; set; }
    }

    public enum GateCondition
    {
        Present,
        Absent
    }

    public class GateConfig
    {
        public string TargetId { get; set; } = string.Empty;
        public GateCondition Condition { get; set; } = GateCondition.Present;
        public double? Confidence { get; set; }
    }
}
