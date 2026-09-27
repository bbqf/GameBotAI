#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 106 (FR-012, SC-003, research R-005): each action of the session-input route has the input
/// time limit. A hung action gives a time-out result, and the service does not send the actions after it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionManagerInputTimeoutTests {
  private const int LimitMs = 300;

  private static InputAction Tap() => new("tap", new Dictionary<string, object> { ["x"] = 10, ["y"] = 20 });

  private static (SessionManager Manager, DeviceLivenessTracker Tracker, string Id) Build(FakeAdbSessionClient device, int adbRetries = 0) {
    var tracker = new DeviceLivenessTracker(SeamSessionManager.NewClock());
    var mgr = SeamSessionManager.Create(device, tracker, new DeviceLivenessOptions { InputTimeoutMs = LimitMs }, adbRetries);
    return (mgr, tracker, SeamSessionManager.CreateDeviceSession(mgr));
  }

  [Fact]
  public async Task AHungTapTimesOutAndTheActionsAfterItAreNotSent() {
    var device = new FakeAdbSessionClient { HangOnCall = 2 };
    var (mgr, tracker, id) = Build(device);

    var sw = Stopwatch.StartNew();
    var result = await mgr.SendInputsWithResultsAsync(id, new[] { Tap(), Tap(), Tap(), Tap() });

    sw.ElapsedMilliseconds.Should().BeLessThan(LimitMs + 2000);
    result.Results.Should().HaveCount(2);
    result.Results[0].Should().Be(new InputActionResult(0, true, null));
    result.Results[1].Should().Be(new InputActionResult(1, false, "tap: device did not answer in 300 ms", TimedOut: true));
    device.InputCalls.Should().Be(2, "the service does not send the actions after the timed-out action");
    tracker.Sample(id, true).LastInputOutcome.Should().Be(InputOutcome.TimedOut);
  }

  [Fact]
  public async Task ACallerCancelBeforeTheLimitIsNotATimeOut() {
    var device = new FakeAdbSessionClient { HangInputs = true };
    var tracker = new DeviceLivenessTracker(SeamSessionManager.NewClock());
    var mgr = SeamSessionManager.Create(device, tracker, new DeviceLivenessOptions { InputTimeoutMs = 10000 });
    var id = SeamSessionManager.CreateDeviceSession(mgr);
    using var cts = new CancellationTokenSource();

    var call = mgr.SendInputsWithResultsAsync(id, new[] { Tap() }, cts.Token);
    await device.FirstInputStarted.WaitAsync(TimeSpan.FromSeconds(5));
    await cts.CancelAsync();

    var act = async () => await call;
    await act.Should().ThrowAsync<OperationCanceledException>("a cancel by the caller is not a time-out result");
    tracker.Sample(id, true).LastInputOutcome.Should().Be(InputOutcome.Cancelled);
  }

  [Fact]
  public async Task TheRetriesOfOneActionShareTheOneLimit() {
    // The first attempt fails at once, the retry hangs: together they must stay in one limit.
    var device = new FakeAdbSessionClient { ExitCode = 1, HangOnCall = 2 };
    var (mgr, _, id) = Build(device, adbRetries: 3);

    var sw = Stopwatch.StartNew();
    var result = await mgr.SendInputsWithResultsAsync(id, new[] { Tap() });

    sw.ElapsedMilliseconds.Should().BeLessThan(LimitMs + 2000);
    result.Results.Should().ContainSingle().Which.TimedOut.Should().BeTrue();
    device.InputCalls.Should().Be(2, "no retry starts after the limit fired");
  }

  [Fact]
  public async Task ACompletedActionHasNoTimeOut() {
    var (mgr, tracker, id) = Build(new FakeAdbSessionClient());

    var result = await mgr.SendInputsWithResultsAsync(id, new[] { Tap(), Tap() });

    result.Results.Should().HaveCount(2).And.OnlyContain(r => r.Dispatched && !r.TimedOut);
    tracker.Sample(id, true).LastInputOutcome.Should().Be(InputOutcome.Completed);
  }
}
