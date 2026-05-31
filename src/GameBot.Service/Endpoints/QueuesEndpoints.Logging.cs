using Microsoft.Extensions.Logging;

namespace GameBot.Service.Endpoints;

internal static partial class QueuesEndpointsLogging {
  [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Queue {QueueId} started (emulator {Serial})")]
  internal static partial void LogQueueStarted(this ILogger logger, string QueueId, string Serial);

  [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Queue {QueueId} stopped")]
  internal static partial void LogQueueStopped(this ILogger logger, string QueueId);
}
