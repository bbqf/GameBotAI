namespace GameBot.Service.Services.Liveness;

/// <summary>
/// One direct screenshot for the liveness probe of the health call (feature 106, research R-007).
/// It does not use <c>ISessionManager.GetSnapshotAsync</c>, because that method returns a stub PNG
/// when the capture fails. Contract tests replace it.
/// </summary>
internal interface ISessionDirectCapture {
  /// <summary>
  /// Returns true when the device returned a PNG with at least one byte. An error or an empty PNG gives
  /// false. The caller applies the time limit through <paramref name="ct"/>.
  /// </summary>
  Task<bool> TryCaptureAsync(string deviceSerial, CancellationToken ct);
}
