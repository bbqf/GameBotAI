namespace GameBot.Service.Services.EnsureGameRunning;

/// <summary>
/// Narrow ADB interface for game-state operations used by <see cref="EnsureGameRunningActionHandler"/>.
/// Isolates ADB platform dependency from the handler and enables mocking in tests.
/// </summary>
internal interface IAdbGameOperations {
  /// <summary>Returns the foreground package on the given device, or null if unavailable.</summary>
  Task<string?> GetForegroundPackageAsync(string deviceSerial, CancellationToken ct = default);

  /// <summary>Launches the app with <paramref name="packageName"/> on the given device (fire-and-forget).</summary>
  Task LaunchAppAsync(string deviceSerial, string packageName, CancellationToken ct = default);
}
