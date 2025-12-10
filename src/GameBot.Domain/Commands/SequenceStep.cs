namespace GameBot.Domain.Commands
{
    /// <summary>
    /// Minimal step model; detailed validation and behaviors added in US1/US2/US3 phases.
    /// </summary>
    public class SequenceStep
    {
        public int Order { get; set; }
        public string CommandId { get; set; } = string.Empty;
        public int? DelayMs { get; set; }
        public DelayRangeMs? DelayRangeMs { get; set; }
        public int? TimeoutMs { get; set; }
        public RetryPolicy? Retry { get; set; }
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
}
