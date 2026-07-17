using System.Diagnostics;
using GameBot.Domain.Actions;
using GameBot.Domain.Config;
using GameBot.Emulator.Adb;

namespace GameBot.Service.Services.EnsureEmulatorRunning;

/// <summary>
/// Ensures a target LDPlayer instance is running and responsive, starting a stopped instance or
/// restarting a hung one, then waiting for boot-complete (feature 070). Health scope is device-only
/// (FR-015): this handler never queries or launches a game/app package — that stays the job of
/// <c>ensure-game-running</c>.
/// </summary>
internal sealed class EnsureEmulatorRunningActionHandler : IEnsureEmulatorRunningActionHandler {
  private readonly IEmulatorControl _control;
  private readonly IEmulatorDeviceProbe _probe;
  private readonly AppConfig _config;

  public EnsureEmulatorRunningActionHandler(IEmulatorControl control, IEmulatorDeviceProbe probe, AppConfig config) {
    _control = control;
    _probe = probe;
    _config = config;
  }

  public async Task<EnsureEmulatorRunningActionResult> ExecuteAsync(EnsureEmulatorRunningArgs args, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(args);

    if (!OperatingSystem.IsWindows())
      return Result(EnsureEmulatorRunningOutcome.PlatformUnsupported);
    if (!_control.IsAvailable || !_probe.IsAvailable)
      return Result(EnsureEmulatorRunningOutcome.ControlUnavailable);

    var runState = await _control.GetRunStateAsync(args.InstanceName, args.InstanceIndex, ct).ConfigureAwait(false);
    if (runState == LdConsoleRunState.NotFound)
      return Result(EnsureEmulatorRunningOutcome.InstanceNotFound);

    if (runState == LdConsoleRunState.Running
        && await _probe.IsResponsiveAsync(args.AdbSerial, ct).ConfigureAwait(false))
      return Result(EnsureEmulatorRunningOutcome.AlreadyHealthy);

    // Not running -> launch; running-but-unresponsive (hung) -> reboot.
    var remediation = runState == LdConsoleRunState.Running
      ? EnsureEmulatorRunningOutcome.Restarted
      : EnsureEmulatorRunningOutcome.Started;
    await RemediateAsync(remediation, args, ct).ConfigureAwait(false);

    return await WaitForHealthyAsync(args.AdbSerial, ct).ConfigureAwait(false)
      ? Result(remediation)
      : Result(EnsureEmulatorRunningOutcome.RecoveryTimedOut);
  }

  private Task RemediateAsync(EnsureEmulatorRunningOutcome remediation, EnsureEmulatorRunningArgs args, CancellationToken ct) =>
    remediation == EnsureEmulatorRunningOutcome.Restarted
      ? _control.RebootAsync(args.InstanceName, args.InstanceIndex, ct)
      : _control.LaunchAsync(args.InstanceName, args.InstanceIndex, ct);

  /// <summary>Polls responsiveness every <c>EmulatorPollIntervalMs</c> up to <c>EmulatorBootWaitMs</c>.</summary>
  private async Task<bool> WaitForHealthyAsync(string adbSerial, CancellationToken ct) {
    var bootWaitMs = Math.Max(0, _config.EmulatorBootWaitMs);
    var pollMs = Math.Max(1, _config.EmulatorPollIntervalMs);
    var sw = Stopwatch.StartNew();
    while (true) {
      if (await _probe.IsResponsiveAsync(adbSerial, ct).ConfigureAwait(false)) return true;
      if (sw.ElapsedMilliseconds >= bootWaitMs) return false;
      try {
        await Task.Delay(pollMs, ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        return false;
      }
    }
  }

  private static EnsureEmulatorRunningActionResult Result(EnsureEmulatorRunningOutcome outcome) => new(outcome);
}
