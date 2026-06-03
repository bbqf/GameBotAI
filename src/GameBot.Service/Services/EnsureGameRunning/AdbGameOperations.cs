using GameBot.Emulator.Adb;

namespace GameBot.Service.Services.EnsureGameRunning;

internal sealed class AdbGameOperations : IAdbGameOperations {
  public async Task<string?> GetForegroundPackageAsync(string deviceSerial, CancellationToken ct = default) {
    if (!OperatingSystem.IsWindows()) return null;
    var adb = new AdbClient().WithSerial(deviceSerial);
    return await adb.GetForegroundPackageAsync(ct).ConfigureAwait(false);
  }

  public async Task LaunchAppAsync(string deviceSerial, string packageName, CancellationToken ct = default) {
    if (!OperatingSystem.IsWindows()) return;
    var adb = new AdbClient().WithSerial(deviceSerial);
    await adb.LaunchAppAsync(packageName, ct).ConfigureAwait(false);
  }
}
