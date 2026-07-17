using System.Diagnostics;
using System.Runtime.Versioning;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Config;
using GameBot.Service.Services.EnsureEmulatorRunning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.IntegrationTests;

/// <summary>
/// Exercises the ensure-emulator-running handler wired to its REAL adapters
/// (<see cref="LdConsoleEmulatorControl"/> + <see cref="AdbEmulatorDeviceProbe"/>) against a
/// nonexistent instance/serial. Validates the graceful-degradation contract (SC-007 / FR-011): the
/// step never throws or hangs and returns a defined neutral/failure outcome — whether ldconsole is
/// absent (CI: ControlUnavailable) or present with a bogus instance (dev host: InstanceNotFound), or
/// on a non-Windows host (PlatformUnsupported).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EnsureEmulatorRunningIntegrationTests {
  [Fact]
  public async Task RealAdaptersDegradeGracefullyForUnknownInstance() {
    var config = new AppConfig { EmulatorProbeTimeoutMs = 200, EmulatorBootWaitMs = 200, EmulatorPollIntervalMs = 50 };
    var control = new LdConsoleEmulatorControl(NullLogger<LdConsoleEmulatorControl>.Instance);
    var probe = new AdbEmulatorDeviceProbe(config);
    var handler = new EnsureEmulatorRunningActionHandler(control, probe, config);
    var args = new EnsureEmulatorRunningArgs {
      InstanceName = $"gamebot-nonexistent-{Guid.NewGuid():N}",
      AdbSerial = "emulator-59999"
    };

    var sw = Stopwatch.StartNew();
    var act = async () => await handler.ExecuteAsync(args, CancellationToken.None);
    var result = await act.Should().NotThrowAsync();
    sw.Stop();

    result.Subject.Outcome.Should().BeOneOf(
      EnsureEmulatorRunningOutcome.ControlUnavailable,
      EnsureEmulatorRunningOutcome.InstanceNotFound,
      EnsureEmulatorRunningOutcome.RecoveryTimedOut,
      EnsureEmulatorRunningOutcome.PlatformUnsupported);
    // Must not block indefinitely even in the worst case (bounded by the small boot wait above).
    sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
  }
}
