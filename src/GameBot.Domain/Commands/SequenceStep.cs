using System.Collections.Generic;

namespace GameBot.Domain.Commands
{
    public enum SequenceStepType
    {
        Command,
        Action,
        Conditional
    }

    public sealed class SequenceActionPayload
    {
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, object?> Parameters { get; } = new();
    }

    /// <summary>
    /// Minimal step model; detailed validation and behaviors added in US1/US2/US3 phases.
    /// </summary>
    public class SequenceStep
    {
        public int Order { get; set; }
        public string CommandId { get; set; } = string.Empty;
        public SequenceStepType StepType { get; set; } = SequenceStepType.Command;
        public SequenceActionPayload? Action { get; set; }
        public ImageVisibleCondition? ConditionExpression { get; set; }
        public int? DelayMs { get; set; }
        public DelayRangeMs? DelayRangeMs { get; set; }
        public int? TimeoutMs { get; set; }
        public RetryPolicy? Retry { get; set; }
        public GateConfig? Gate { get; set; }
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
