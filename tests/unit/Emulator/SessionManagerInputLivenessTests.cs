#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Config;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Adb;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 106 (FR-001, FR-014, research R-010): each ADB input records its start and its outcome in
/// the liveness tracker, in both input paths. A cancel never leaves the outcome pending.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionManagerInputLivenessTests : IDisposable {
  private readonly string? _previousUseAdb;

  public SessionManagerInputLivenessTests() {
    _previousUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
  }

  public void Dispose() => Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _previousUseAdb);

  private static InputAction Tap() => new("tap", new Dictionary<string, object> { ["x"] = 10, ["y"] = 20 });

  [Fact]
  public async Task ACompletedTapRecordsCompleted() {
    var clock = SeamSessionManager.NewClock();
    var tracker = new DeviceLivenessTracker(clock);
    var mgr = SeamSessionManager.Create(new FakeAdbSessionClient(), tracker);
    var id = SeamSessionManager.CreateDeviceSession(mgr);

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { Tap() });

    result.Results[0].Dispatched.Should().BeTrue();
    var s = tracker.Sample(id, true);
    s.LastInputOutcome.Should().Be(InputOutcome.Completed);
    s.LastInputAt.Should().Be(clock.GetUtcNow());
  }

  [Fact]
  public async Task ATapThatFailsOnAllAttemptsRecordsFailed() {
    var tracker = new DeviceLivenessTracker(SeamSessionManager.NewClock());
    var device = new FakeAdbSessionClient { ExitCode = 1 };
    var mgr = SeamSessionManager.Create(device, tracker, adbRetries: 2);
    var id = SeamSessionManager.CreateDeviceSession(mgr);

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { Tap() });

    result.Results[0].Dispatched.Should().BeFalse();
    device.InputCalls.Should().Be(3);
    tracker.Sample(id, true).LastInputOutcome.Should().Be(InputOutcome.Failed);
  }

  [Fact]
  public async Task TheSequencePathRecordsTheInputAndKeepsItsReturnValue() {
    var tracker = new DeviceLivenessTracker(SeamSessionManager.NewClock());
    var mgr = SeamSessionManager.Create(new FakeAdbSessionClient(), tracker);
    var id = SeamSessionManager.CreateDeviceSession(mgr);

    var executed = await mgr.SendInputsAsync(id, new[] { Tap(), Tap() });

    executed.Should().Be(2);
    tracker.Sample(id, true).LastInputOutcome.Should().Be(InputOutcome.Completed);
  }

  [Fact]
  public async Task AHungSequenceTapCancelledBeforeTheLimitRecordsCancelled() {
    var tracker = new DeviceLivenessTracker(SeamSessionManager.NewClock());
    var device = new FakeAdbSessionClient { HangInputs = true };
    var mgr = SeamSessionManager.Create(device, tracker);
    var id = SeamSessionManager.CreateDeviceSession(mgr);
    using var cts = new CancellationTokenSource();

    var call = mgr.SendInputsAsync(id, new[] { Tap() }, cts.Token);
    await device.FirstInputStarted.WaitAsync(TimeSpan.FromSeconds(5));
    tracker.Sample(id, true).LastInputOutcome.Should().Be(InputOutcome.Pending);
    await cts.CancelAsync();

    var act = async () => await call;
    await act.Should().ThrowAsync<OperationCanceledException>();
    tracker.Sample(id, true).LastInputOutcome.Should().Be(InputOutcome.Cancelled);
  }

  [Fact]
  public async Task AHungSequenceTapCancelledAfterTheLimitRecordsTimedOut() {
    var clock = SeamSessionManager.NewClock();
    var tracker = new DeviceLivenessTracker(clock);
    var device = new FakeAdbSessionClient { HangInputs = true };
    var mgr = SeamSessionManager.Create(device, tracker, new DeviceLivenessOptions { InputTimeoutMs = 10000 });
    var id = SeamSessionManager.CreateDeviceSession(mgr);
    using var cts = new CancellationTokenSource();

    var call = mgr.SendInputsAsync(id, new[] { Tap() }, cts.Token);
    await device.FirstInputStarted.WaitAsync(TimeSpan.FromSeconds(5));
    clock.Advance(TimeSpan.FromSeconds(11));
    await cts.CancelAsync();

    var act = async () => await call;
    await act.Should().ThrowAsync<OperationCanceledException>();
    tracker.Sample(id, true).LastInputOutcome.Should().Be(InputOutcome.TimedOut,
      "a sequence watchdog that stops a hung input keeps the fault signal");
  }

  [Fact]
  public async Task StubModeRecordsNothing() {
    var tracker = new DeviceLivenessTracker(SeamSessionManager.NewClock());
    var mgr = new SessionManager(Options.Create(new SessionOptions()), NullLogger<SessionManager>.Instance,
      NullLogger<AdbClient>.Instance, new AppConfig(), tracker);
    var session = mgr.CreateSession("game");

    await mgr.SendInputsAsync(session.Id, new[] { Tap() });
    await mgr.SendInputsWithResultsAsync(session.Id, new[] { Tap() });

    var s = tracker.Sample(session.Id, false);
    s.LastInputAt.Should().BeNull();
    s.LastInputOutcome.Should().BeNull();
  }

  [Fact]
  public async Task ASessionWithNoDeviceSerialRecordsNothingOnTheSeam() {
    var tracker = new DeviceLivenessTracker(SeamSessionManager.NewClock());
    var device = new FakeAdbSessionClient();
    var mgr = SeamSessionManager.Create(device, tracker);
    var id = SeamSessionManager.CreateDeviceSession(mgr, serial: null);

    await mgr.SendInputsAsync(id, new[] { Tap() });

    device.InputCalls.Should().Be(0);
    tracker.Sample(id, false).LastInputOutcome.Should().BeNull();
  }

  [Fact]
  public async Task StopSessionRemovesTheRecord() {
    var tracker = new DeviceLivenessTracker(SeamSessionManager.NewClock());
    var mgr = SeamSessionManager.Create(new FakeAdbSessionClient(), tracker);
    var id = SeamSessionManager.CreateDeviceSession(mgr);
    await mgr.SendInputsAsync(id, new[] { Tap() });

    mgr.StopSession(id).Should().BeTrue();

    tracker.Sample(id, true).LastInputAt.Should().BeNull();
  }
}
