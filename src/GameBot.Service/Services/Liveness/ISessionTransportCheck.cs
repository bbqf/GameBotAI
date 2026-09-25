namespace GameBot.Service.Services.Liveness;

/// <summary>
/// The result of one transport check (feature 106, data-model section 7). The values are the values of
/// the <c>adb</c> block of the session health response.
/// </summary>
/// <param name="Ok">True when the device reports the state <c>device</c>.</param>
/// <param name="Stdout">The standard output of <c>adb get-state</c>. Null when the command did not run.</param>
/// <param name="Stderr">The standard error of <c>adb get-state</c>. Null when the command did not run.</param>
/// <param name="Error">The error text when the command could not run or did not answer. Null otherwise.</param>
internal sealed record SessionTransportCheckResult(bool Ok, string? Stdout, string? Stderr, string? Error);

/// <summary>Checks the ADB transport of one device (feature 106). Contract tests replace it.</summary>
internal interface ISessionTransportCheck {
  /// <summary>Runs <c>adb get-state</c> for <paramref name="deviceSerial"/>. The caller applies the time limit through <paramref name="ct"/>.</summary>
  Task<SessionTransportCheckResult> CheckAsync(string deviceSerial, CancellationToken ct);
}
