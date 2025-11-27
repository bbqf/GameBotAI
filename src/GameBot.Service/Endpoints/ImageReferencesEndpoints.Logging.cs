using Microsoft.Extensions.Logging;

namespace GameBot.Service.Endpoints;

internal static partial class ImageReferencesEndpointsLogging
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Warning, Message = "Image upload rejected: missing id/data")] 
    internal static partial void LogUploadRejectedMissingFields(this ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Image upload failed for id {Id}")] 
    internal static partial void LogUploadFailed(this ILogger logger, Exception exception, string Id);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Image {Id} persisted (size={Size} bytes, overwrite={Overwrite})")] 
    internal static partial void LogImagePersisted(this ILogger logger, string Id, int Size, bool Overwrite);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Image {Id} resolved from store")] 
    internal static partial void LogImageResolvedFromStore(this ILogger logger, string Id);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "Image {Id} resolved from disk fallback")] 
    internal static partial void LogImageResolvedFromDiskFallback(this ILogger logger, string Id);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Image {Id} not found")] 
    internal static partial void LogImageNotFound(this ILogger logger, string Id);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Image {Id} deleted")] 
    internal static partial void LogImageDeleted(this ILogger logger, string Id);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Delete request for missing image {Id}")] 
    internal static partial void LogDeleteRequestMissing(this ILogger logger, string Id);
}
