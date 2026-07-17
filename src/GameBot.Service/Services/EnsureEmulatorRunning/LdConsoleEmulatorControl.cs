using GameBot.Emulator.Adb;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.EnsureEmulatorRunning;

/// <summary>
/// Windows adapter of <see cref="IEmulatorControl"/> backed by <see cref="LdConsoleClient"/>. Resolves
/// <c>ldconsole.exe</c> once at construction; <see cref="IsAvailable"/> is false when it cannot be
/// located, which the handler maps to a neutral "control unavailable" outcome.
/// </summary>
internal sealed class LdConsoleEmulatorControl : IEmulatorControl {
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

  public async Task RebootAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) {
    if (!OperatingSystem.IsWindows() || _path is null) return;
    await new LdConsoleClient(_path, _logger).RebootAsync(instanceName, instanceIndex, ct).ConfigureAwait(false);
  }
}
