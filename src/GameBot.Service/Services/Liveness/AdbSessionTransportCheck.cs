using GameBot.Emulator.Adb;

namespace GameBot.Service.Services.Liveness;

/// <summary>
/// The production <see cref="ISessionTransportCheck"/>: runs <c>adb get-state</c> (feature 106). The
/// logic came from the session health endpoint and gives the same <c>ok</c>, <c>stdout</c>,
/// <c>stderr</c> and <c>error</c> values.
/// </summary>
internal sealed class AdbSessionTransportCheck : ISessionTransportCheck {
  private readonly ILogger<AdbClient> _adbLogger;

  public AdbSessionTransportCheck(ILogger<AdbClient> adbLogger) {
    _adbLogger = adbLogger;
  }

  public async Task<SessionTransportCheckResult> CheckAsync(string deviceSerial, CancellationToken ct) {
    if (!OperatingSystem.IsWindows()) {
      return new SessionTransportCheckResult(false, null, null, "adb is not supported on this platform");
    }
    try {
      var adb = new AdbClient(_adbLogger).WithSerial(deviceSerial);
      var (code, stdout, stderr) = await adb.ExecAsync("get-state", ct).ConfigureAwait(false);
      var ok = code == 0 && stdout.Trim().Equals("device", StringComparison.OrdinalIgnoreCase);
      return new SessionTransportCheckResult(ok, stdout, stderr, null);
    }
    catch (InvalidOperationException ex) {
      return new SessionTransportCheckResult(false, null, null, ex.Message);
    }
    catch (System.ComponentModel.Win32Exception ex) {
      // The adb executable was not found or could not start.
      return new SessionTransportCheckResult(false, null, null, ex.Message);
    }
  }
}
