using Microsoft.Extensions.Logging;

namespace GameBot.Service.Endpoints;

internal static partial class QueuesEndpointsLogging {
  [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Queue {QueueId} started (emulator {Serial})")]
  internal static partial void LogQueueStarted(this ILogger logger, string QueueId, string Serial);

  [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Queue {QueueId} stopped")]
  internal static partial void LogQueueStopped(this ILogger logger, string QueueId);

  // Feature 171 (FR-022): the operator pause. The loop logs the moment it reaches the gate (event 1134).
  [LoggerMessage(EventId = 1103, Level = LogLevel.Information, Message = "Queue {QueueId} pause requested (operator). The running sequence finishes first.")]
  internal static partial void LogQueuePauseRequested(this ILogger logger, string QueueId);

  [LoggerMessage(EventId = 1104, Level = LogLevel.Information, Message = "Queue {QueueId} {Action} (operator pause).")]
  internal static partial void LogQueuePauseReleased(this ILogger logger, string QueueId, string Action);

  // Feature 079 (FR-017): a refused start launches no run, so it produces no execution-log entry.
  // Record it in the application log instead, naming everything needed to resolve the conflict.
  [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
    Message = "Queue {QueueId} start refused: emulator {DeviceSerial} is held by queue {HoldingQueueId} ({HoldingQueueName}).")]
  internal static partial void LogQueueStartRefusedDeviceInUse(this ILogger logger, string QueueId, string DeviceSerial, string HoldingQueueId, string HoldingQueueName);
}
