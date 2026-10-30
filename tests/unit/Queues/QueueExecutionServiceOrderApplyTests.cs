using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA2000

namespace GameBot.UnitTests.Queues;

// Feature 171 (User Story 4): after an order apply and a resume, the once-per-run pass follows the new order. An entry
// that ran in this cycle does not run again, and no next due time moves.
public sealed partial class QueueExecutionServiceTests {
  [Fact]
  public async Task AfterAnOrderApplyThePassFollowsTheNewOrderAndTheEntryThatRanDoesNotRunAgain() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithId(OncePerRun("B"), "2"), WithId(OncePerRun("C"), "3") });
    var (handle, _) = await PauseAfterFirstAsync(h);

    ApplyAll(handle, TargetTemplate(WithId(OncePerRun("C"), "3"), WithId(OncePerRun("B"), "2"), WithId(OncePerRun("A"), "1")));
    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "C", "B");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task AnOrderApplyDoesNotMoveATimerAndDoesNotCountACycle() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] {
      WithId(TimerEntry("D", TimeOnly.FromDateTime(FakeStart.DateTime.AddHours(-1))), "1"),
      WithId(TimerEntry("X", new TimeOnly(23, 59)), "2")
    }, cycle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("D"));
    var handle = await WaitForHandleAsync(h, "q1");
    var dueBefore = handle.Schedule!.ComputeNextDue(handle, clock.GetLocalNow());
    await PauseIdleLoopAsync(handle, clock);

    ApplyAll(handle, TargetTemplate(
      WithId(TimerEntry("X", new TimeOnly(23, 59)), "2"),
      WithId(TimerEntry("D", TimeOnly.FromDateTime(FakeStart.DateTime.AddHours(-1))), "1")));
    var dueAfter = handle.Schedule.ComputeNextDue(handle, clock.GetLocalNow());
    handle.ReleaseOperatorPause();
    await AdvanceInStepsAsync(clock, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30));
    await h.Service.StopAsync("q1");

    dueAfter.Should().Be(dueBefore);
    h.Sequences.Executed.Count(id => id == "D").Should().Be(1, "the order apply did not reset the timer of D");
    handle.Schedule.Plan.Keys.Should().Equal("2", "1");
  }
}
