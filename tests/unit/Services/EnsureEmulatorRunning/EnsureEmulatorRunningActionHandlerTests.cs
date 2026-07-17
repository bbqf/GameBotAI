using System.Runtime.Versioning;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Config;
using GameBot.Emulator.Adb;
using GameBot.Service.Services.EnsureEmulatorRunning;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Services.EnsureEmulatorRunning;

[SupportedOSPlatform("windows")]
public sealed class EnsureEmulatorRunningActionHandlerTests {
  private sealed class FakeControl : IEmulatorControl {
    public bool IsAvailable { get; set; } = true;
    public LdConsoleRunState RunState { get; set; } = LdConsoleRunState.Running;
    public int Launches { get; private set; }
    public int Reboots { get; private set; }
    public Task<LdConsoleRunState> GetRunStateAsync(string? n, int? i, CancellationToken ct = default) => Task.FromResult(RunState);
    public Task LaunchAsync(string? n, int? i, CancellationToken ct = default) { Launches++; return Task.CompletedTask; }
    public Task RebootAsync(string? n, int? i, CancellationToken ct = default) { Reboots++; return Task.CompletedTask; }
  }

  private sealed class FakeProbe : IEmulatorDeviceProbe {
    private readonly Queue<bool> _results = new();
    public bool IsAvailable { get; set; } = true;
    public int Calls { get; private set; }
    public void Enqueue(params bool[] values) { foreach (var v in values) _results.Enqueue(v); }
    public Task<bool> IsResponsiveAsync(string serial, CancellationToken ct = default) {
      Calls++;
      return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : false);
    }
  }

  private static AppConfig FastConfig() =>
    new() { EmulatorProbeTimeoutMs = 10, EmulatorBootWaitMs = 40, EmulatorPollIntervalMs = 1 };

  private static EnsureEmulatorRunningArgs Args() =>
    new() { AdbSerial = "emulator-5558", InstanceIndex = 1 };

  [Fact]
  public async Task AlreadyHealthyWhenRunningAndResponsive() {
    var control = new FakeControl { RunState = LdConsoleRunState.Running };
    var probe = new FakeProbe();
    probe.Enqueue(true);
    var result = await new EnsureEmulatorRunningActionHandler(control, probe, FastConfig()).ExecuteAsync(Args());
    result.Outcome.Should().Be(EnsureEmulatorRunningOutcome.AlreadyHealthy);
    result.IsSuccess.Should().BeTrue();
    control.Launches.Should().Be(0);
    control.Reboots.Should().Be(0);
  }

  [Fact]
  public async Task StartsStoppedInstanceThenSucceeds() {
    var control = new FakeControl { RunState = LdConsoleRunState.Stopped };
    var probe = new FakeProbe();
    probe.Enqueue(true); // first poll after launch is healthy
    var result = await new EnsureEmulatorRunningActionHandler(control, probe, FastConfig()).ExecuteAsync(Args());
    result.Outcome.Should().Be(EnsureEmulatorRunningOutcome.Started);
    result.IsSuccess.Should().BeTrue();
    control.Launches.Should().Be(1);
    control.Reboots.Should().Be(0);
  }

  [Fact]
  public async Task RebootsHungInstanceThenSucceeds() {
    var control = new FakeControl { RunState = LdConsoleRunState.Running };
    var probe = new FakeProbe();
    probe.Enqueue(false, true); // health check says hung, then poll after reboot is healthy
    var result = await new EnsureEmulatorRunningActionHandler(control, probe, FastConfig()).ExecuteAsync(Args());
    result.Outcome.Should().Be(EnsureEmulatorRunningOutcome.Restarted);
    result.IsSuccess.Should().BeTrue();
    control.Reboots.Should().Be(1);
    control.Launches.Should().Be(0);
  }

  [Fact]
  public async Task RecoveryTimesOutWhenNeverHealthy() {
    var control = new FakeControl { RunState = LdConsoleRunState.Stopped };
    var probe = new FakeProbe(); // always false
    var result = await new EnsureEmulatorRunningActionHandler(control, probe, FastConfig()).ExecuteAsync(Args());
    result.Outcome.Should().Be(EnsureEmulatorRunningOutcome.RecoveryTimedOut);
    result.IsSuccess.Should().BeFalse();
    control.Launches.Should().Be(1);
  }

  [Fact]
  public async Task InstanceNotFoundFailsWithoutRemediation() {
    var control = new FakeControl { RunState = LdConsoleRunState.NotFound };
    var probe = new FakeProbe();
    var result = await new EnsureEmulatorRunningActionHandler(control, probe, FastConfig()).ExecuteAsync(Args());
    result.Outcome.Should().Be(EnsureEmulatorRunningOutcome.InstanceNotFound);
    result.IsSuccess.Should().BeFalse();
    control.Launches.Should().Be(0);
    control.Reboots.Should().Be(0);
  }

  [Fact]
  public async Task ControlUnavailableWhenLdConsoleMissing() {
    var control = new FakeControl { IsAvailable = false };
    var result = await new EnsureEmulatorRunningActionHandler(control, new FakeProbe(), FastConfig()).ExecuteAsync(Args());
    result.Outcome.Should().Be(EnsureEmulatorRunningOutcome.ControlUnavailable);
    result.IsUnsupported.Should().BeTrue();
  }

  [Fact]
  public async Task ControlUnavailableWhenProbeUnavailable() {
    var probe = new FakeProbe { IsAvailable = false };
    var result = await new EnsureEmulatorRunningActionHandler(new FakeControl(), probe, FastConfig()).ExecuteAsync(Args());
    result.Outcome.Should().Be(EnsureEmulatorRunningOutcome.ControlUnavailable);
    result.IsUnsupported.Should().BeTrue();
  }

  [Fact]
  public void HandlerHasNoGameOrAppDependencies() {
    // FR-015: emulator health only — the handler must never depend on a game/app-package surface.
    // Use the simple type name (not the full name, whose "GameBot" namespace would false-match "Game").
    var paramTypeNames = typeof(EnsureEmulatorRunningActionHandler)
      .GetConstructors().Single().GetParameters()
      .Select(p => p.ParameterType.Name);
    paramTypeNames.Should().NotContain(t => t.Contains("Game", StringComparison.OrdinalIgnoreCase)
      || t.Contains("Package", StringComparison.OrdinalIgnoreCase));
  }
}
