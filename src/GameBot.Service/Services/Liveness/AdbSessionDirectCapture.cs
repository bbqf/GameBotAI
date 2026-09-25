using GameBot.Emulator.Adb;
using GameBot.Emulator.Session;

namespace GameBot.Service.Services.Liveness;

/// <summary>
/// The production <see cref="ISessionDirectCapture"/>: one <c>adb exec-out screencap</c> through
/// <see cref="AdbScreenCaptureProvider"/> (feature 106).
/// </summary>
internal sealed class AdbSessionDirectCapture : ISessionDirectCapture {
  private readonly ILogger<AdbClient> _adbLogger;

  public AdbSessionDirectCapture(ILogger<AdbClient> adbLogger) {
    _adbLogger = adbLogger;
  }

  public async Task<bool> TryCaptureAsync(string deviceSerial, CancellationToken ct) {
    if (!OperatingSystem.IsWindows()) return false;
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
