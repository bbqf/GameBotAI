namespace GameBot.Service.Services.EnsureEmulatorRunning;

/// <summary>
/// Decision helper for the connect-to-game emulator pre-heal (feature 071). Keeps the proceed/fail-fast
/// rule and the result-message clause in one testable place, shared with
/// <c>SequenceExecutionService.DispatchConnectToGameAsync</c>.
/// </summary>
internal static class ConnectEmulatorPreheat {
  /// <summary>
  /// Returns a fail reason when the pre-heal was a genuine failure (so the connect must NOT start a
  /// session), or <c>null</c> to proceed. <c>null</c> input means no pre-heal ran (no instance id).
  /// </summary>
  public static string? FailFastReason(EnsureEmulatorRunningActionResult? emu) =>
    emu is not null && !emu.IsSuccess && !emu.IsUnsupported
      ? $"connect-to-game emulator pre-heal failed: {emu.ReasonCode}"
      : null;

  /// <summary>The <c>emulator: &lt;reason&gt;; </c> clause for the connect success message, or empty when no pre-heal ran.</summary>
  public static string MessageClause(EnsureEmulatorRunningActionResult? emu) =>
    emu is not null ? $"emulator: {emu.ReasonCode}; " : string.Empty;
}
