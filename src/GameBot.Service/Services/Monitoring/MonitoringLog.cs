using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.Monitoring;

/// <summary>
/// Feature 141 (FR-017): log entries of the emulator monitoring. Each entry shows the session id and the
/// result. A successful screenshot request is not logged. The logger category is
/// <see cref="MonitoringLoggingTag"/>.
/// </summary>
internal static partial class MonitoringLog {
  [LoggerMessage(EventId = 7500, Level = LogLevel.Information, Message = "Restart requested for session {SessionId} (serial {Serial}).")]
  public static partial void RestartRequested(ILogger logger, string SessionId, string Serial);

  [LoggerMessage(EventId = 7501, Level = LogLevel.Warning, Message = "Restart refused for session {SessionId}: {Result}.")]
  public static partial void RestartRefused(ILogger logger, string SessionId, string Result);

  [LoggerMessage(EventId = 7502, Level = LogLevel.Information, Message = "Restart step for session {SessionId}: {Step}.")]
  public static partial void RestartStep(ILogger logger, string SessionId, string Step);

  [LoggerMessage(EventId = 7503, Level = LogLevel.Information, Message = "Restart of session {SessionId} ended with result {Result}.")]
  public static partial void RestartSucceeded(ILogger logger, string SessionId, string Result);

  [LoggerMessage(EventId = 7504, Level = LogLevel.Warning, Message = "Restart of session {SessionId} ended with result failure: {Result}.")]
  public static partial void RestartFailed(ILogger logger, string SessionId, string Result);

  [LoggerMessage(EventId = 7505, Level = LogLevel.Warning, Message = "Screenshot request for session {SessionId} failed with result {Result}.")]
  public static partial void ScreenshotFailed(ILogger logger, string SessionId, string Result);
}

/// <summary>The logger category of the emulator monitoring.</summary>
internal sealed class MonitoringLoggingTag { }
