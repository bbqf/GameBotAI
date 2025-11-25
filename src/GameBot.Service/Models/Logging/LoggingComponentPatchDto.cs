using Microsoft.Extensions.Logging;

namespace GameBot.Service.Models.Logging;

internal sealed record class LoggingComponentPatchDto
{
    public LogLevel? Level { get; init; }
    public bool? Enabled { get; init; }
    public string? Notes { get; init; }
}