namespace GameBot.Service.Models.Logging;

internal sealed record class LoggingPolicyResetRequestDto
{
    public string? Reason { get; init; }
}