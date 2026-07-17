using System.Runtime.Versioning;
using GameBot.Domain.Config;
using GameBot.Emulator.Adb;

namespace GameBot.Service.Services.EnsureEmulatorRunning;

/// <summary>
/// Windows adapter of <see cref="IEmulatorDeviceProbe"/> backed by <see cref="AdbClient"/>. A device is
/// responsive when it is present in <c>adb devices</c> with state <c>device</c> AND reports
/// <c>sys.boot_completed=1</c>, both within <see cref="AppConfig.EmulatorProbeTimeoutMs"/>.
/// </summary>
internal sealed class AdbEmulatorDeviceProbe : IEmulatorDeviceProbe {
  private readonly AppConfig _config;

  public AdbEmulatorDeviceProbe(AppConfig config) {
    _config = config;
  }

  private static readonly char[] DeviceLineSeparators = { ' ', '\t' };

  public bool IsAvailable => OperatingSystem.IsWindows();

  public async Task<bool> IsResponsiveAsync(string adbSerial, CancellationToken ct = default) {
    if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(adbSerial)) return false;
    var timeoutMs = Math.Max(1, _config.EmulatorProbeTimeoutMs);
    using var timeout = new CancellationTokenSource(timeoutMs);
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
    try {
      if (!await IsDevicePresentAsync(adbSerial, linked.Token).ConfigureAwait(false)) return false;
      return await IsBootCompletedAsync(adbSerial, linked.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      return false;
    }
    catch (InvalidOperationException) {
      return false;
    }
  }

  [SupportedOSPlatform("windows")]
  private static async Task<bool> IsDevicePresentAsync(string adbSerial, CancellationToken ct) {
    var (code, stdout, _) = await new AdbClient().ExecAsync("devices", ct).ConfigureAwait(false);
    return code == 0 && IsSerialOnline(stdout, adbSerial);
  }

  [SupportedOSPlatform("windows")]
  private static async Task<bool> IsBootCompletedAsync(string adbSerial, CancellationToken ct) {
    var (code, stdout, _) = await new AdbClient().WithSerial(adbSerial)
      .ExecAsync("shell getprop sys.boot_completed", ct).ConfigureAwait(false);
    return code == 0 && stdout.Trim() == "1";
  }

  /// <summary>Returns true when <paramref name="serial"/> appears in <c>adb devices</c> output with state <c>device</c>.</summary>
  internal static bool IsSerialOnline(string adbDevicesOutput, string serial) {
    foreach (var line in adbDevicesOutput.Split('\n')) {
      var trimmed = line.Trim();
      if (trimmed.Length == 0 || trimmed.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
      var parts = trimmed.Split(DeviceLineSeparators, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length < 2) continue;
      if (string.Equals(parts[0], serial, StringComparison.OrdinalIgnoreCase)
          && string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
        return true;
    }
    return false;
  }
}
