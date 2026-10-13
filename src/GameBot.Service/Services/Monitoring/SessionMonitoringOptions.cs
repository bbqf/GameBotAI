namespace GameBot.Service.Services.Monitoring;

/// <summary>
/// Feature 141: the settings of the emulator monitoring. The configuration section is
/// <see cref="SectionName"/>. Use <see cref="Normalized"/> before you read the values.
/// </summary>
internal sealed class SessionMonitoringOptions {
  /// <summary>The configuration section of these options.</summary>
  public const string SectionName = "Service:Monitoring";

  internal const int DefaultRestartTimeoutMs = 120000;
  internal const int DefaultRestartPollIntervalMs = 3000;

  /// <summary>The total time limit of one emulator restart, in milliseconds.</summary>
  public int RestartTimeoutMs { get; set; } = DefaultRestartTimeoutMs;

  /// <summary>The wait between two device checks during a restart, in milliseconds.</summary>
  public int RestartPollIntervalMs { get; set; } = DefaultRestartPollIntervalMs;

  /// <summary>
  /// Returns a valid copy. A timeout that is not greater than 0 becomes the default. A poll interval that is
  /// not greater than 0, or not less than the timeout, becomes the default or the largest valid value.
  /// </summary>
  public SessionMonitoringOptions Normalized() {
    var timeout = RestartTimeoutMs > 0 ? RestartTimeoutMs : DefaultRestartTimeoutMs;
    var poll = RestartPollIntervalMs > 0 ? RestartPollIntervalMs : DefaultRestartPollIntervalMs;
    if (poll >= timeout) poll = Math.Max(1, timeout - 1);
    return new SessionMonitoringOptions { RestartTimeoutMs = timeout, RestartPollIntervalMs = poll };
  }
}
