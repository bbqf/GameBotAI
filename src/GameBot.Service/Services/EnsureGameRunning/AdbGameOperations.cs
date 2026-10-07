using System.Text.RegularExpressions;
using GameBot.Emulator.Adb;

namespace GameBot.Service.Services.EnsureGameRunning;

internal sealed partial class AdbGameOperations : IAdbGameOperations {
  [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)]
  private static partial Regex SafePackageRegex();

  /// <summary>
  /// True when <paramref name="packageName"/> is a plain Android package name.
  /// The name goes into an adb shell command, so any other text is not safe.
  /// </summary>
  public static bool IsSafePackageName(string? packageName) =>
    !string.IsNullOrEmpty(packageName) && SafePackageRegex().IsMatch(packageName);

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

  public async Task<bool> ForceStopAppAsync(string deviceSerial, string packageName, CancellationToken ct = default) {
    if (!CanRun(deviceSerial, packageName)) return false;
    var adb = new AdbClient().WithSerial(deviceSerial);
    var (exit, _, _) = await adb.ForceStopAppAsync(packageName, ct).ConfigureAwait(false);
    return exit == 0;
  }

  public async Task<bool> TryLaunchAppAsync(string deviceSerial, string packageName, CancellationToken ct = default) {
    if (!CanRun(deviceSerial, packageName)) return false;
    var adb = new AdbClient().WithSerial(deviceSerial);
    var (exit, _, _) = await adb.LaunchAppAsync(packageName, ct).ConfigureAwait(false);
    return exit == 0;
  }

  // Run no process for a blank serial (adb would pick any device), an unsafe name, or a non-Windows host.
  private static bool CanRun(string deviceSerial, string packageName) =>
    OperatingSystem.IsWindows()
    && !string.IsNullOrWhiteSpace(deviceSerial)
    && IsSafePackageName(packageName);
}
