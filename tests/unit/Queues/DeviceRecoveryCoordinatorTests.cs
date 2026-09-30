#pragma warning disable CA2007, CA2000 // test code: no ConfigureAwait; the coordinator lives for one test
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.QueueExecution;
using GameBot.UnitTests.Queues.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 121 (FR-011, FR-012, research R-006): the recovery slot. The tests use real time with a short
/// stagger, because the stagger wait uses the timer of the time provider.
/// </summary>
public sealed class DeviceRecoveryCoordinatorTests {
  private static DeviceRecoveryCoordinator Create(FakeEmulatorControl control, int staggerMs) =>
    new(control, Options.Create(new DeviceLivenessOptions { RecoveryStaggerMs = staggerMs }));

  [Fact]
  public async Task AReboot_CallsTheControlOnceWithTheInstanceName() {
    var control = new FakeEmulatorControl();
    var sut = Create(control, 0);

    (await sut.RebootInstanceAsync("LDPlayer-1", CancellationToken.None)).Should().BeTrue();

    control.Reboots.Should().Be(1);
    control.Starts.Single().Name.Should().Be("LDPlayer-1");
  }

  [Fact]
  public async Task AFailedReboot_GivesFalse() {
    var control = new FakeEmulatorControl { Result = false };

    (await Create(control, 0).RebootInstanceAsync("A", CancellationToken.None)).Should().BeFalse();
  }

  [Fact]
  public async Task AThrowingControl_GivesFalseAndFreesTheSlot() {
    var control = new FakeEmulatorControl { Throws = new InvalidOperationException("boom") };
    var sut = Create(control, 0);

    (await sut.RebootInstanceAsync("A", CancellationToken.None)).Should().BeFalse();
    control.Throws = null;
    (await sut.RebootInstanceAsync("B", CancellationToken.None)).Should().BeTrue();
  }

  [Fact]
  public async Task TwoInstances_StartOneAtATime() {
    var gate = new TaskCompletionSource();
    var control = new FakeEmulatorControl { Gate = gate.Task };
    var sut = Create(control, 0);

    var first = sut.RebootInstanceAsync("A", CancellationToken.None);
    await WaitUntilAsync(() => control.Reboots == 1);
    var second = sut.RebootInstanceAsync("B", CancellationToken.None);
    await Task.Delay(150);

    control.Reboots.Should().Be(1, "the slot is busy");
    gate.SetResult();
    (await first).Should().BeTrue();
    (await second).Should().BeTrue();
    control.Reboots.Should().Be(2);
  }

  [Fact]
  public async Task TheGapBetweenTwoStartTimes_IsTheStaggerOrMore() {
    var control = new FakeEmulatorControl();
    var sut = Create(control, 400);

    var a = sut.RebootInstanceAsync("A", CancellationToken.None);
    var b = sut.RebootInstanceAsync("B", CancellationToken.None);
    await Task.WhenAll(a, b);

    var starts = control.Starts.Select(s => s.StartedAt).OrderBy(t => t).ToArray();
    starts.Should().HaveCount(2);
    // The stagger is 400 ms. A small slack covers the clock resolution.
    (starts[1] - starts[0]).TotalMilliseconds.Should().BeGreaterThanOrEqualTo(380);
  }

  [Fact]
  public async Task ACallForTheSameInstance_SharesOneReboot() {
    var gate = new TaskCompletionSource();
    var control = new FakeEmulatorControl { Gate = gate.Task };
    var sut = Create(control, 0);

    var first = sut.RebootInstanceAsync("LDPlayer-1", CancellationToken.None);
    await WaitUntilAsync(() => control.Reboots == 1);
    var second = sut.RebootInstanceAsync("ldplayer-1", CancellationToken.None);
    gate.SetResult();

    (await first).Should().BeTrue();
    (await second).Should().BeTrue();
    control.Reboots.Should().Be(1, "the instance name ignores the case");
  }

  [Fact]
  public async Task ACancelDuringTheStaggerWait_FreesTheSlot() {
    var control = new FakeEmulatorControl();
    var sut = Create(control, 60000);
    (await sut.RebootInstanceAsync("A", CancellationToken.None)).Should().BeTrue();
    using var cts = new CancellationTokenSource();
    var waiting = sut.RebootInstanceAsync("B", cts.Token);
    await Task.Delay(100);

    await cts.CancelAsync();

    await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
    control.Reboots.Should().Be(1, "the cancelled reboot did not start");
    // A new call can take the slot again (it waits for the stagger, so cancel it too).
    using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
    await FluentActions.Awaiting(() => sut.RebootInstanceAsync("C", cts2.Token)).Should().ThrowAsync<OperationCanceledException>();
  }

  [Fact]
  public async Task ACancelAfterTheStart_FreesTheSlotWhenTheCommandEnds() {
    var gate = new TaskCompletionSource();
    var control = new FakeEmulatorControl { Gate = gate.Task };
    var sut = Create(control, 0);
    using var cts = new CancellationTokenSource();
    var first = sut.RebootInstanceAsync("A", cts.Token);
    await WaitUntilAsync(() => control.Reboots == 1);

    await cts.CancelAsync();
    await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();
    gate.SetResult();

    (await sut.RebootInstanceAsync("B", CancellationToken.None)).Should().BeTrue();
  }

  private static async Task WaitUntilAsync(Func<bool> condition) {
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!condition()) {
      if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition did not become true.");
      await Task.Delay(10);
    }
  }
}
