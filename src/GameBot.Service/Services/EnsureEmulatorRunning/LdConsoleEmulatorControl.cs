using GameBot.Emulator.Adb;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.EnsureEmulatorRunning;

/// <summary>
/// Windows adapter of <see cref="IEmulatorControl"/> backed by <see cref="LdConsoleClient"/>. Resolves
/// <c>ldconsole.exe</c> once at construction; <see cref="IsAvailable"/> is false when it cannot be
/// located, which the handler maps to a neutral "control unavailable" outcome.
/// </summary>
internal sealed partial class LdConsoleEmulatorControl : IEmulatorControl {
  private readonly ILogger<LdConsoleEmulatorControl> _logger;
  private readonly string? _path;

  public LdConsoleEmulatorControl(ILogger<LdConsoleEmulatorControl> logger) {
    _logger = logger;
    _path = OperatingSystem.IsWindows() ? LdConsoleResolver.ResolveLdConsolePath() : null;
  }

  public bool IsAvailable => OperatingSystem.IsWindows() && _path is not null;

  public async Task<LdConsoleRunState> GetRunStateAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) {
    if (!OperatingSystem.IsWindows() || _path is null) return LdConsoleRunState.NotFound;
    return await new LdConsoleClient(_path, _logger).IsRunningAsync(instanceName, instanceIndex, ct).ConfigureAwait(false);
  }

  public async Task LaunchAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) {
    if (!OperatingSystem.IsWindows() || _path is null) return;
    await new LdConsoleClient(_path, _logger).LaunchAsync(instanceName, instanceIndex, ct).ConfigureAwait(false);
  }

  /// <summary>
  /// Test seam: uses <paramref name="pathOverride"/> as the tool path and does not resolve the path
  /// on the host.
  /// </summary>
  internal LdConsoleEmulatorControl(ILogger<LdConsoleEmulatorControl> logger, string? pathOverride) {
    _logger = logger;
    _path = pathOverride;
  }

  /// <summary>
  /// Restarts the instance. Returns true when the tool ends with exit code 0. Returns false when the
  /// tool is missing, the exit code is not 0, or the process cannot start. A cancel is not caught.
  /// </summary>
  public async Task<bool> RebootAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) {
    if (!OperatingSystem.IsWindows() || _path is null) return false;
    try {
      var (code, _, _) = await new LdConsoleClient(_path, _logger).RebootAsync(instanceName, instanceIndex, ct).ConfigureAwait(false);
      return code == 0;
    }
    catch (OperationCanceledException) {
      throw;
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) {
      RebootStartFailed(_logger, ex);
      return false;
    }
  }

  [LoggerMessage(EventId = 1160, Level = LogLevel.Warning, Message = "Instance reboot could not start the ldconsole process.")]
  private static partial void RebootStartFailed(ILogger logger, Exception ex);
}
