using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.Notifications;

/// <summary>
/// Structured application-log entries for outbound failure notifications (feature 087, FR-015a).
/// <para>
/// The live health block carries the same outcome, but the two audiences are different: an operator
/// tailing logs and one polling the API must each be able to learn that an alert did not get out.
/// An alerting mechanism that fails silently is the exact problem this feature exists to fix.
/// </para>
/// <para>
/// <b>No message here interpolates the configured auth header value or the receiver's response
/// body.</b> Only the destination host, the queue, and the failure reason.
/// </para>
/// </summary>
internal static partial class FailureNotificationLogging {
  [LoggerMessage(EventId = 1110, Level = LogLevel.Information,
    Message = "Failure notification delivered for queue {QueueId} to {DestinationHost} ({EventType}).")]
  internal static partial void LogNotificationDelivered(this ILogger logger, string? QueueId, string DestinationHost, string EventType);

  [LoggerMessage(EventId = 1111, Level = LogLevel.Warning,
    Message = "Failure notification for queue {QueueId} to {DestinationHost} was abandoned after {Attempts} attempt(s): {Reason}")]
  internal static partial void LogNotificationAbandoned(this ILogger logger, string? QueueId, string DestinationHost, int Attempts, string Reason);

  [LoggerMessage(EventId = 1112, Level = LogLevel.Warning,
    Message = "Failure notification for queue {QueueId} could not be sent: no destination configured.")]
  internal static partial void LogNotificationNoDestination(this ILogger logger, string? QueueId);
}
