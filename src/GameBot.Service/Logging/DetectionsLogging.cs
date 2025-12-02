using Microsoft.Extensions.Logging;

namespace GameBot.Service.Logging
{
    internal static partial class DetectionsLogging
    {
        public const string Category = "GameBot.Service.ImageDetections";

        [LoggerMessage(EventId = 10100, Level = LogLevel.Information, Message = "Runtime arch: x64={Is64Bit}, arch={Arch}; OpenCV: {CvFirstLine}")]
        public static partial void LogDetectionRuntimeInfo(this ILogger logger, bool is64Bit, string arch, string cvFirstLine);
    }
}
