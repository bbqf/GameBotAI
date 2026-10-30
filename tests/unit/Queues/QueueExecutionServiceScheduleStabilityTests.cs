using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA2000

namespace GameBot.UnitTests.Queues;

// Feature 171 (FR-008, FR-009): a pause, an apply, and a resume never reset, move, or skip a timer. These tests use a
// fake clock and a cycling run with timer entries only, so the loop waits in its poll delay between firings.
public sealed partial class QueueExecutionServiceTests {
  private static readonly TimeOnly EarlierToday = new(11, 0);

  /// <summary>Asks for a pause and moves the fake clock in small steps until the loop reaches the gate.</summary>
  private static async Task PauseIdleLoopAsync(QueueRunHandle handle, FakeTimeProvider clock) {
    handle.RequestOperatorPause(clock.GetLocalNow());
    for (var i = 0; i < 100 && handle.OperatorPause != OperatorPauseState.Paused; i++) {
      clock.Advance(TimeSpan.FromMilliseconds(300));
      await Task.Delay(10);
    }
    handle.OperatorPause.Should().Be(OperatorPauseState.Paused);
  }

  private static async Task AdvanceInStepsAsync(FakeTimeProvider clock, TimeSpan total, TimeSpan step) {
    for (var elapsed = TimeSpan.Zero; elapsed < total; elapsed += step) {
      clock.Advance(step);
      await Task.Delay(5);
    }
  }

  private static QueueTemplateEntry ParameterisedTimer(string sequence, TimeOnly at, string id, string slot) =>
    WithSlot(WithId(TimerEntry(sequence, at), id), slot);

  [Fact]
  public async Task ATimeOfDayTimerThatFiredTodayDoesNotFireAgainAfterAPauseAnApplyAndAResume() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] {
      WithId(TimerEntry("D", EarlierToday), "1"),
      ParameterisedTimer("X", new TimeOnly(23, 59), "2", "old")
    }, cycle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("D"));
    var handle = await WaitForHandleAsync(h, "q1");
    var nextDueBefore = handle.Schedule!.ComputeNextDue(handle, clock.GetLocalNow());
    await PauseIdleLoopAsync(handle, clock);

    ApplyAll(handle, TargetTemplate(WithId(TimerEntry("D", EarlierToday), "1"), ParameterisedTimer("X", new TimeOnly(23, 59), "2", "new")));
    var nextDueAfterApply = handle.Schedule.ComputeNextDue(handle, clock.GetLocalNow());
    handle.ReleaseOperatorPause();
    await AdvanceInStepsAsync(clock, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));

    h.Sequences.Executed.Count(id => id == "D").Should().Be(1, "D fired today, and the apply did not reset it");
    nextDueAfterApply.Should().Be(nextDueBefore);

    clock.Advance(TimeSpan.FromHours(23.5));
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "D") == 2);
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Count(id => id == "D").Should().Be(2, "D fires again the next day, once");
  }

  [Fact]
  public async Task AnEntryThatIsDisabledAndEnabledAgainKeepsItsConsumedState() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] {
      WithId(TimerEntry("D", EarlierToday), "1"),
      WithId(TimerEntry("X", new TimeOnly(23, 59)), "2")
    }, cycle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("D"));
    var handle = await WaitForHandleAsync(h, "q1");

    await PauseIdleLoopAsync(handle, clock);
    var disabledD = WithId(TimerEntry("D", EarlierToday), "1");
    disabledD.Enabled = false;
    ApplyAll(handle, TargetTemplate(disabledD, WithId(TimerEntry("X", new TimeOnly(23, 59)), "2")));
    handle.ReleaseOperatorPause();
    await AdvanceInStepsAsync(clock, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30));

    await PauseIdleLoopAsync(handle, clock);
    ApplyAll(handle, TargetTemplate(WithId(TimerEntry("D", EarlierToday), "1"), WithId(TimerEntry("X", new TimeOnly(23, 59)), "2")));
    handle.ReleaseOperatorPause();
    await AdvanceInStepsAsync(clock, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30));
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Count(id => id == "D").Should().Be(1, "a disable and an enable do not reset the timer");
  }

  [Fact]
  public async Task ANewEntryStartsAsTheTemplateStatesAndTheOldEntriesKeepTheirSchedules() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] {
      WithId(TimerEntry("D", EarlierToday), "1"),
      WithId(TimerEntry("X", new TimeOnly(23, 59)), "2")
    }, cycle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("D"));
    var handle = await WaitForHandleAsync(h, "q1");
    var nextDueBefore = handle.Schedule!.ComputeNextDue(handle, clock.GetLocalNow());
    await PauseIdleLoopAsync(handle, clock);

    // The new entry states 10:00, which has passed today: it runs at once, as an entry in a template does at the start.
    ApplyAll(handle, TargetTemplate(
      WithId(TimerEntry("D", EarlierToday), "1"),
      WithId(TimerEntry("X", new TimeOnly(23, 59)), "2"),
      WithId(TimerEntry("N", new TimeOnly(10, 0)), "3")));
    handle.ReleaseOperatorPause();
    await WaitForAsync(() => h.Sequences.Executed.Contains("N"));
    await AdvanceInStepsAsync(clock, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30));
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Should().Equal("D", "N");
    nextDueBefore.Should().NotBeNull();
  }

  [Fact]
  public async Task WithTheScheduleKeyTheEntryStartsItsNewScheduleAndWithoutItKeepsTheOld() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] {
      WithId(TimerEntry("D", EarlierToday), "1"),
      WithId(TimerEntry("X", new TimeOnly(23, 59)), "2")
    }, cycle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("D"));
    var handle = await WaitForHandleAsync(h, "q1");
    await PauseIdleLoopAsync(handle, clock);

    // D moves to 11:30. The first apply does not select the schedule key: D keeps 11:00, and its state.
    var moved = TargetTemplate(WithId(TimerEntry("D", new TimeOnly(11, 30)), "1"), WithId(TimerEntry("X", new TimeOnly(23, 59)), "2"));
    var diff = QueueTemplateDiff.Compute(handle.Schedule!.Plan, moved, new Dictionary<string, string>());
    diff.Differences.Should().ContainSingle().Which.Key.Should().Be("schedule:1");
    var withoutKey = QueueLiveApplyBuilder.Build(handle.Schedule.Plan, moved, new HashSet<string>());
    withoutKey.AllEntries[0].TimerTimeOfDay.Should().Be(EarlierToday);

    // The second apply selects it: D gets 11:30 and its consumed state clears, so it fires again at once (11:30 has passed? no: now is 12:00).
    var built = QueueLiveApplyBuilder.Build(handle.Schedule.Plan, moved, new HashSet<string> { "schedule:1" });
    handle.Schedule.SwapPlan(QueueRunPlan.FromAll(built.AllEntries, built.AllKeys, handle.Schedule.Plan.Version + 1), built.DroppedKeys, built.ClearedKeys);
    handle.ReleaseOperatorPause();
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "D") == 2);
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Count(id => id == "D").Should().Be(2, "the new schedule starts fresh, and 11:30 has passed");
  }
}
