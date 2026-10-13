using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Adb;
using GameBot.Service.Endpoints.Dto;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.Liveness;
using GameBot.Service.Services.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests;

public sealed class SessionRestartServiceTests {
  private sealed class FakeControl : IEmulatorControl {
    private readonly List<string> _calls;
    public FakeControl(List<string> calls) { _calls = calls; }
    public bool IsAvailable { get; set; } = true;
    public bool RebootResult { get; set; } = true;
    public LdConsoleRunState RunState { get; set; } = LdConsoleRunState.Running;
    public TaskCompletionSource<bool>? RebootGate { get; set; }
    public List<int?> RebootedIndexes { get; } = new();
    public Task<LdConsoleRunState> GetRunStateAsync(string? n, int? i, CancellationToken ct = default) => Task.FromResult(RunState);
    public Task LaunchAsync(string? n, int? i, CancellationToken ct = default) => Task.CompletedTask;
    public async Task<bool> RebootAsync(string? n, int? i, CancellationToken ct = default) {
      _calls.Add("reboot");
      RebootedIndexes.Add(i);
      if (RebootGate is not null) await RebootGate.Task.WaitAsync(ct);
      return RebootResult;
    }
  }

  private sealed class FakeProbe : IEmulatorDeviceProbe {
    private readonly List<string> _calls;
    public FakeProbe(List<string> calls) { _calls = calls; }
    public bool IsAvailable => true;
    public bool Responsive { get; set; } = true;
    public Task<bool> IsResponsiveAsync(string adbSerial, CancellationToken ct = default) {
      _calls.Add("probe");
      return Task.FromResult(Responsive);
    }
  }

  private sealed class FakeReconnector : ISessionReconnector {
    private readonly List<string> _calls;
    public FakeReconnector(List<string> calls) { _calls = calls; }
    public bool Throws { get; set; }
    public List<(string SessionId, string Serial)> Reconnects { get; } = new();
    public Task ReconnectAsync(string sessionId, string deviceSerial, CancellationToken ct = default) {
      _calls.Add("reconnect");
      Reconnects.Add((sessionId, deviceSerial));
      if (Throws) throw new InvalidOperationException("boom");
      return Task.CompletedTask;
    }
  }

  private sealed class FakeCapture : ISessionDirectCapture {
    private readonly List<string> _calls;
    public FakeCapture(List<string> calls) { _calls = calls; }
    public Task<bool> TryCaptureAsync(string deviceSerial, CancellationToken ct) {
      _calls.Add("capture");
      return Task.FromResult(true);
    }
  }

  private sealed class CapturingLogger : ILogger<MonitoringLoggingTag> {
    public List<string> Messages { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
      lock (Messages) Messages.Add(formatter(state, exception));
    }
  }

  private sealed class Rig {
    public List<string> Calls { get; } = new();
    public FakeControl Control { get; }
    public FakeProbe Probe { get; }
    public FakeReconnector Reconnector { get; }
    public CapturingLogger Logger { get; } = new();
    public SessionRestartService Service { get; }

    public Rig(int timeoutMs = 5000, int pollMs = 1) {
      Control = new FakeControl(Calls);
      Probe = new FakeProbe(Calls);
      Reconnector = new FakeReconnector(Calls);
      Service = new SessionRestartService(
        Control, Probe, Reconnector, new FakeCapture(Calls),
        Options.Create(new SessionMonitoringOptions { RestartTimeoutMs = timeoutMs, RestartPollIntervalMs = pollMs }),
        TimeProvider.System, Logger);
    }
  }

  private static EmulatorSession Session(string id, string? serial) =>
    new() { Id = id, GameId = "pns", DeviceSerial = serial, Status = SessionStatus.Running };

  [Fact]
  public async Task SuccessCallsRebootThenProbeThenReconnectThenCapture() {
    var rig = new Rig();

    var result = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);
    await result.Completion;

    result.Status.Should().Be(RestartStartStatus.Accepted);
    var record = rig.Service.Get("a")!;
    record.State.Should().Be(RestartStates.Success);
    record.Step.Should().Be(RestartSteps.Done);
    record.Reason.Should().BeNull();
    record.EndedAtUtc.Should().NotBeNull();
    rig.Calls.Should().ContainInOrder("reboot", "probe", "reconnect", "capture");
    rig.Reconnector.Reconnects.Should().ContainSingle().Which.Should().Be(("a", "emulator-5558"));
  }

  [Fact]
  public async Task NeverRespondingDeviceEndsAsTimeout() {
    var rig = new Rig(timeoutMs: 150, pollMs: 10);
    rig.Probe.Responsive = false;

    var result = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);
    await result.Completion;

    var record = rig.Service.Get("a")!;
    record.State.Should().Be(RestartStates.Failure);
    record.Reason.Should().Be(RestartReasons.Timeout);
    rig.Reconnector.Reconnects.Should().BeEmpty();
  }

  [Fact]
  public async Task RebootErrorEndsAsRebootFailed() {
    var rig = new Rig();
    rig.Control.RebootResult = false;

    var result = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);
    await result.Completion;

    var record = rig.Service.Get("a")!;
    record.State.Should().Be(RestartStates.Failure);
    record.Reason.Should().Be(RestartReasons.RebootFailed);
    rig.Calls.Should().NotContain("reconnect");
  }

  [Fact]
  public async Task ReconnectErrorEndsAsReconnectFailed() {
    var rig = new Rig();
    rig.Reconnector.Throws = true;

    var result = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);
    await result.Completion;

    var record = rig.Service.Get("a")!;
    record.State.Should().Be(RestartStates.Failure);
    record.Reason.Should().Be(RestartReasons.ReconnectFailed);
  }

  [Fact]
  public async Task PollIntervalControlsTheWaitBetweenDeviceChecks() {
    var rig = new Rig(timeoutMs: 5000, pollMs: 200);

    var started = DateTimeOffset.UtcNow;
    var result = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);
    await result.Completion;
    var elapsed = DateTimeOffset.UtcNow - started;

    // The device check waits one poll interval before the first probe.
    elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    rig.Service.Get("a")!.State.Should().Be(RestartStates.Success);
  }

  [Fact]
  public async Task SecondRequestWhileRestartingIsRefusedAndRecordStays() {
    var rig = new Rig();
    var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Control.RebootGate = gate;
    var first = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);
    var before = rig.Service.Get("a")!;

    var second = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);

    second.Status.Should().Be(RestartStartStatus.InProgress);
    rig.Service.Get("a")!.StartedAtUtc.Should().Be(before.StartedAtUtc);
    rig.Service.Get("a")!.State.Should().Be(RestartStates.Restarting);
    rig.Control.RebootedIndexes.Should().HaveCountLessThanOrEqualTo(1);

    gate.SetResult(true);
    await first.Completion;
    rig.Service.Get("a")!.State.Should().Be(RestartStates.Success);
  }

  [Fact]
  public async Task NewRestartIsAllowedAfterTheLastOneEnded() {
    var rig = new Rig();
    var first = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);
    await first.Completion;

    var second = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);
    await second.Completion;

    second.Status.Should().Be(RestartStartStatus.Accepted);
    rig.Control.RebootedIndexes.Should().HaveCount(2);
  }

  [Fact]
  public async Task RestartOfSessionANeverTouchesTheInstanceOfSessionB() {
    var rig = new Rig();
    var a = Session("a", "emulator-5558");
    var b = Session("b", "emulator-5560");

    var result = await rig.Service.StartAsync(a, CancellationToken.None);
    await result.Completion;

    rig.Control.RebootedIndexes.Should().ContainSingle().Which.Should().Be(2);
    rig.Service.Get("b").Should().BeNull();
    rig.Reconnector.Reconnects.Should().OnlyContain(r => r.SessionId == a.Id);
    b.DeviceSerial.Should().Be("emulator-5560");
  }

  [Theory]
  [InlineData(null, "no_device_serial")]
  [InlineData("192.168.1.5:5555", "unsupported_serial")]
  public async Task SessionWithoutALdPlayerSerialIsRefusedAndNothingChanges(string? serial, string reason) {
    var rig = new Rig();

    var result = await rig.Service.StartAsync(Session("a", serial), CancellationToken.None);

    result.Status.Should().Be(RestartStartStatus.Unavailable);
    result.UnavailableReason.Should().Be(reason);
    rig.Calls.Should().BeEmpty();
    rig.Service.Get("a").Should().BeNull();
  }

  [Fact]
  public async Task MissingLdConsoleIsRefused() {
    var rig = new Rig();
    rig.Control.IsAvailable = false;

    var result = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);

    result.Status.Should().Be(RestartStartStatus.Unavailable);
    result.UnavailableReason.Should().Be(RestartUnavailableReasons.LdConsoleMissing);
    rig.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task InstanceThatIsNotRunningIsRefused() {
    var rig = new Rig();
    rig.Control.RunState = LdConsoleRunState.NotFound;

    var result = await rig.Service.StartAsync(Session("a", "emulator-5558"), CancellationToken.None);

    result.Status.Should().Be(RestartStartStatus.Unavailable);
    result.UnavailableReason.Should().Be(RestartUnavailableReasons.InstanceNotFound);
    rig.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task EachStepIsLoggedWithTheSessionId() {
    var rig = new Rig();

    var result = await rig.Service.StartAsync(Session("sess-77", "emulator-5558"), CancellationToken.None);
    await result.Completion;

    rig.Logger.Messages.Should().OnlyContain(m => m.Contains("sess-77", StringComparison.Ordinal));
    foreach (var step in new[] { "rebooting", "waiting_for_device", "reconnecting", "checking_capture" }) {
      rig.Logger.Messages.Should().Contain(m => m.Contains(step, StringComparison.Ordinal), step);
    }
    rig.Logger.Messages.Should().Contain(m => m.Contains("requested", StringComparison.Ordinal));
    rig.Logger.Messages.Should().Contain(m => m.Contains("success", StringComparison.Ordinal));
  }

  [Fact]
  public async Task PruneDropsEndedRecordsOfRemovedSessionsOnly() {
    var rig = new Rig();
    var done = await rig.Service.StartAsync(Session("gone", "emulator-5558"), CancellationToken.None);
    await done.Completion;

    rig.Service.Prune(new List<string> { "other" });

    rig.Service.Get("gone").Should().BeNull();
  }
}
