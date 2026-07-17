namespace GameBot.Service.Services.EnsureEmulatorRunning;

/// <summary>
/// Narrow seam over the ADB responsiveness probe used by
/// <see cref="EnsureEmulatorRunningActionHandler"/>. Isolates the Windows/ADB dependency and enables
/// fakes in tests.
/// </summary>
internal interface IEmulatorDeviceProbe {
  /// <summary><c>true</c> when ADB probing is possible on this host.</summary>
  bool IsAvailable { get; }

  /// <summary>
  /// Returns <c>true</c> when the device with <paramref name="adbSerial"/> is present (state
  /// <c>device</c>, not offline/absent) AND reports <c>sys.boot_completed=1</c> within the configured
  /// probe timeout. Any failure/timeout returns <c>false</c> (treated as unresponsive/hung).
  /// </summary>
  Task<bool> IsResponsiveAsync(string adbSerial, CancellationToken ct = default);
}
