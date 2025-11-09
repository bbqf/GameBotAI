namespace GameBot.Domain.Profiles;

public enum TriggerStatus
{
    Pending,
    Satisfied,
    Cooldown,
    Disabled
}

public sealed class TriggerEvaluationResult
{
    public required TriggerStatus Status { get; set; }
    public double? Similarity { get; set; }
    public double? Confidence { get; set; }
    public string? Reason { get; set; }
    public required DateTimeOffset EvaluatedAt { get; set; }
}
