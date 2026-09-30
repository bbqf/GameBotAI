using GameBot.Emulator;
using GameBot.Emulator.Adb;
using GameBot.Emulator.Session;

namespace GameBot.Service.Services.Liveness;

/// <summary>
/// The production <see cref="ISessionDirectCapture"/>: one <c>adb exec-out screencap</c> through
/// <see cref="AdbScreenCaptureProvider"/> (feature 106).
/// </summary>
internal sealed class AdbSessionDirectCapture : ISessionDirectCapture {
  private readonly ILogger<AdbClient> _adbLogger;
  private readonly DeviceCaptureGate _gate;

  public AdbSessionDirectCapture(ILogger<AdbClient> adbLogger, DeviceCaptureGate? gate = null) {
    _adbLogger = adbLogger;
    _gate = gate ?? new DeviceCaptureGate();
  }

  public async Task<bool> TryCaptureAsync(string deviceSerial, CancellationToken ct) {
    if (!OperatingSystem.IsWindows()) return false;
    // Feature 121 (R-014): a Suspect device can still run a screencap process. The probe starts no
    // second process, so each health call cannot add one.
    if (!string.IsNullOrWhiteSpace(deviceSerial) && _gate.IsSuspect(deviceSerial)) return false;
    try {
      var provider = new AdbScreenCaptureProvider(deviceSerial, _adbLogger);
      var png = await provider.CaptureScreenshotPngAsync(ct).ConfigureAwait(false);
      return png is { Length: > 0 };
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
      throw;
    }
    catch (Exception) {
      // Each other failure is a failed capture. The probe reports it as capture_stalled.
      return false;
    }
  }
}
