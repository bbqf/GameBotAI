using Microsoft.Extensions.Logging;

namespace GameBot.Service.Endpoints {
  internal static partial class ImageDetectionsEndpointComponent {
    [LoggerMessage(EventId = 11000, Level = LogLevel.Information, Message = "Detect start id={Id} threshold={Threshold} max={Max} overlap={Overlap}")]
    public static partial void LogDetectStart(this ILogger logger, string Id, double Threshold, int Max, double Overlap);

    [LoggerMessage(EventId = 11001, Level = LogLevel.Information, Message = "Detect results count={Count} limitsHit={LimitsHit} durationMs={DurationMs}")]
    public static partial void LogDetectResults(this ILogger logger, int Count, bool LimitsHit, long DurationMs);

    [LoggerMessage(EventId = 11002, Level = LogLevel.Warning, Message = "Detect invalid request: {Error}")]
    public static partial void LogDetectInvalid(this ILogger logger, string Error);

    [LoggerMessage(EventId = 11003, Level = LogLevel.Warning, Message = "Detect not found: {Id}")]
    public static partial void LogDetectNotFound(this ILogger logger, string Id);

    // Feature 085 (issue #176): a refusal to measure is logged rather than being reported as an
    // empty match set. Without this, the failure the issue describes leaves no trace at all.
    [LoggerMessage(EventId = 11004, Level = LogLevel.Warning, Message = "Detect could not resolve a screen: reason={Reason} id={Id}")]
    public static partial void LogDetectUnresolvedScreen(this ILogger logger, string Reason, string Id);

    // Feature 089 (issue #190): an operator comparing two scores needs to know which comparison
    // they are looking at. Emitted as its own event rather than folded into the results message, so
    // no existing log format is repurposed.
    [LoggerMessage(EventId = 11005, Level = LogLevel.Information, Message = "Detect mask id={Id} masked={Masked} retainedPixelCount={RetainedPixelCount}")]
    public static partial void LogDetectMask(this ILogger logger, string Id, bool Masked, int RetainedPixelCount);
  }
}
