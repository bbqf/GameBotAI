using System.ComponentModel.DataAnnotations;
using GameBot.Domain.Profiles;

namespace GameBot.Service.Models;

internal sealed class RegionDto
{
    [Range(0,1)] public double X { get; set; }
    [Range(0,1)] public double Y { get; set; }
    [Range(0,1)] public double Width { get; set; }
    [Range(0,1)] public double Height { get; set; }
}

internal sealed class ProfileTriggerCreateDto
{
    [Required] public required string Type { get; set; }
    public bool Enabled { get; set; } = true;
    [Range(0,int.MaxValue)] public int CooldownSeconds { get; set; } = 60;
    [Required] public required object Params { get; set; } = default!;
}

internal sealed class ProfileTriggerDto
{
    public required string Id { get; set; }
    public required string Type { get; set; }
    public bool Enabled { get; set; }
    public int CooldownSeconds { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public TriggerEvaluationResult? LastResult { get; set; }
    public required object Params { get; set; } = default!;
}

internal static class TriggerMappings
{
    public static ProfileTriggerDto ToDto(ProfileTrigger t) => new()
    {
        Id = t.Id,
    Type = t.Type.ToString().Replace("Match","-MATCH", StringComparison.Ordinal).ToUpperInvariant(),
        Enabled = t.Enabled,
        CooldownSeconds = t.CooldownSeconds,
        LastFiredAt = t.LastFiredAt,
        LastEvaluatedAt = t.LastEvaluatedAt,
        LastResult = t.LastResult,
        Params = t.Params
    };
}
