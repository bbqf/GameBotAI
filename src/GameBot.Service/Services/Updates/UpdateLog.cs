using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;

namespace GameBot.Service.Services.Updates;

internal static partial class UpdateLog {
  [LoggerMessage(EventId = 7400, Level = LogLevel.Information, Message = "Ignored release tag '{Tag}': it is not a version tag.")]
  public static partial void TagIgnored(ILogger logger, string tag);

  [LoggerMessage(EventId = 7401, Level = LogLevel.Information, Message = "Update check finished: {Status}. Installed {Installed}, latest {Latest}.")]
  public static partial void CheckFinished(ILogger logger, UpdateCheckStatus status, SemanticVersion installed, SemanticVersion latest);

  [LoggerMessage(EventId = 7402, Level = LogLevel.Warning, Message = "Update check failed with code {Code}: {Message}")]
  public static partial void CheckFailed(ILogger logger, string code, string message);

  [LoggerMessage(EventId = 7403, Level = LogLevel.Information, Message = "Update attempt {AttemptId} started: {From} to {Target}.")]
  public static partial void InstallStarted(ILogger logger, Guid attemptId, string from, string target);

  [LoggerMessage(EventId = 7404, Level = LogLevel.Information, Message = "Update attempt {AttemptId} is now in state {State}.")]
  public static partial void InstallState(ILogger logger, Guid attemptId, UpdateState state);

  [LoggerMessage(EventId = 7405, Level = LogLevel.Error, Message = "Update attempt {AttemptId} failed with code {Code}: {Message}")]
  public static partial void InstallFailed(ILogger logger, Guid attemptId, string code, string message);

  [LoggerMessage(EventId = 7406, Level = LogLevel.Warning, Message = "The bot could not stop all queues before the update: {Message}")]
  public static partial void QueueStopFailed(ILogger logger, string message);

  [LoggerMessage(EventId = 7407, Level = LogLevel.Information, Message = "Update result of attempt {AttemptId} is {State}.")]
  public static partial void ResultReported(ILogger logger, Guid attemptId, UpdateState state);

  [LoggerMessage(EventId = 7408, Level = LogLevel.Warning, Message = "The update result file cannot be read: {Message}")]
  public static partial void ResultUnreadable(ILogger logger, string message);

  [LoggerMessage(EventId = 7409, Level = LogLevel.Warning, Message = "Cleanup of old update files failed: {Message}")]
  public static partial void CleanupFailed(ILogger logger, string message);

  [LoggerMessage(EventId = 7410, Level = LogLevel.Information, Message = "Stopped queue {QueueId} for the update.")]
  public static partial void QueueStopped(ILogger logger, string queueId);
}
