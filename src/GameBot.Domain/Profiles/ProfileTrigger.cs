namespace GameBot.Domain.Profiles;

public enum TriggerType
{
    Delay,
    Schedule,
    ImageMatch,
    TextMatch
}

public sealed class Region
{
    public required double X { get; set; } // [0..1]
    public required double Y { get; set; } // [0..1]
    public required double Width { get; set; } // (0..1]
    public required double Height { get; set; } // (0..1]
}

public abstract class TriggerParams { }

public sealed class DelayParams : TriggerParams
{
    public required int Seconds { get; set; }
}

public sealed class ScheduleParams : TriggerParams
{
    public required DateTimeOffset Timestamp { get; set; }
}

public sealed class ImageMatchParams : TriggerParams
{
    public required string ReferenceImageId { get; set; } = string.Empty;
    public required Region Region { get; set; } = default!;
    public double SimilarityThreshold { get; set; } = 0.85;
}

public sealed class TextMatchParams : TriggerParams
{
    public required string Target { get; set; } = string.Empty;
    public required Region Region { get; set; } = default!;
    public double ConfidenceThreshold { get; set; } = 0.80;
    public required string Mode { get; set; } = "found"; // found | not-found
}

public sealed class ProfileTrigger
{
    public required string Id { get; set; }
    public required TriggerType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public int CooldownSeconds { get; set; } = 60;
    public DateTimeOffset? LastFiredAt { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public TriggerEvaluationResult? LastResult { get; set; }
    public required TriggerParams Params { get; set; } = default!;
}
