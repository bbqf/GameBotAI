using Microsoft.Extensions.Logging;

namespace GameBot.Service.Endpoints
{
    internal static partial class ImageDetectionsEndpointComponent
    {
        [LoggerMessage(EventId = 11000, Level = LogLevel.Information, Message = "Detect start id={Id} threshold={Threshold} max={Max} overlap={Overlap}")]
        public static partial void LogDetectStart(this ILogger logger, string Id, double Threshold, int Max, double Overlap);

        [LoggerMessage(EventId = 11001, Level = LogLevel.Information, Message = "Detect results count={Count} limitsHit={LimitsHit} durationMs={DurationMs}")]
        public static partial void LogDetectResults(this ILogger logger, int Count, bool LimitsHit, long DurationMs);

        [LoggerMessage(EventId = 11002, Level = LogLevel.Warning, Message = "Detect invalid request: {Error}")]
        public static partial void LogDetectInvalid(this ILogger logger, string Error);

        [LoggerMessage(EventId = 11003, Level = LogLevel.Warning, Message = "Detect not found: {Id}")]
        public static partial void LogDetectNotFound(this ILogger logger, string Id);
    }
}
