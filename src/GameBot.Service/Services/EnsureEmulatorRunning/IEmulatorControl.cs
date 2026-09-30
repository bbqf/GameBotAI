using GameBot.Emulator.Adb;

namespace GameBot.Service.Services.EnsureEmulatorRunning;

/// <summary>
/// Narrow seam over LDPlayer's <c>ldconsole</c> instance control used by
/// <see cref="EnsureEmulatorRunningActionHandler"/>. Isolates the Windows/ldconsole dependency from
/// the handler's decision logic and enables fakes in tests.
/// </summary>
internal interface IEmulatorControl {
  /// <summary><c>true</c> when <c>ldconsole</c> could be located (control is possible on this host).</summary>
  bool IsAvailable { get; }

  /// <summary>Returns the instance run state, or <see cref="LdConsoleRunState.NotFound"/>.</summary>
  Task<LdConsoleRunState> GetRunStateAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default);

  /// <summary>Starts the addressed instance.</summary>
  Task LaunchAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default);

  /// <summary>
  /// Restarts the addressed instance in place. Returns true when the tool ends with exit code 0.
  /// Returns false when the tool is missing, the exit code is not 0, or the process cannot start.
  /// </summary>
  Task<bool> RebootAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default);
}
