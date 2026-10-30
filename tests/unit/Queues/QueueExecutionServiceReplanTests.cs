using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA2000

namespace GameBot.UnitTests.Queues;

// Feature 171: the run loop reads a new plan after a live apply. These tests swap the plan while the loop waits at the
// operator gate, the way the apply service does. The loop then runs the new plan, and an entry that ran in this cycle
// does not run again.
public sealed partial class QueueExecutionServiceTests {
  private static QueueTemplateEntry WithId(QueueTemplateEntry entry, string id) {
    entry.EntryId = id;
    return entry;
  }

  private static QueueTemplate TargetTemplate(params QueueTemplateEntry[] entries) {
    var template = new QueueTemplate { Id = "target", Name = "target" };
    foreach (var e in entries) template.Entries.Add(e);
    return template;
  }

  /// <summary>Applies every difference between the plan of the run and <paramref name="target"/>, as the apply service does.</summary>
  private static void ApplyAll(QueueRunHandle handle, QueueTemplate target) {
    var schedule = handle.Schedule!;
    var plan = schedule.Plan;
    var names = new Dictionary<string, string>();
    var diff = QueueTemplateDiff.Compute(plan, target, names);
    diff.NeedsUpgrade.Should().BeFalse("the test entries have IDs");
    var built = QueueLiveApplyBuilder.Build(plan, target, new HashSet<string>(diff.Differences.Select(d => d.Key), StringComparer.Ordinal));
    schedule.SwapPlan(QueueRunPlan.FromAll(built.AllEntries, built.AllKeys, plan.Version + 1), built.DroppedKeys, built.ClearedKeys);
  }

  /// <summary>Starts the queue, lets sequence A run and block, asks for a pause, and waits until the loop is paused.</summary>
  private static async Task<(QueueRunHandle Handle, TaskCompletionSource ReleaseA)> PauseAfterFirstAsync(Harness h) {
    var aStarted = new TaskCompletionSource();
    var releaseA = new TaskCompletionSource();
    var firstRun = true;
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A" && firstRun) {
        firstRun = false;
        aStarted.TrySetResult();
        await releaseA.Task.WaitAsync(ct);
      }
      return FakeSequenceExecution.Success(id);
    };
    await h.Service.StartAsync("q1");
    await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var handle = await WaitForHandleAsync(h, "q1");
    handle.RequestOperatorPause(DateTimeOffset.UtcNow);
    releaseA.TrySetResult();
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);
    return (handle, releaseA);
  }

  [Fact]
  public async Task ARemovedEntryNeverRunsAndTheEntriesThatRanDoNotRunAgain() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithId(OncePerRun("B"), "2"), WithId(OncePerRun("C"), "3") });
    var (handle, _) = await PauseAfterFirstAsync(h);

    ApplyAll(handle, TargetTemplate(WithId(OncePerRun("A"), "1"), WithId(OncePerRun("C"), "3")));
    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "C");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task AnAddedEntryRunsAfterTheResume() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithId(OncePerRun("B"), "2") });
    var (handle, _) = await PauseAfterFirstAsync(h);

    ApplyAll(handle, TargetTemplate(WithId(OncePerRun("A"), "1"), WithId(OncePerRun("D"), "4"), WithId(OncePerRun("B"), "2")));
    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "D", "B");
  }

  [Fact]
  public async Task ADisabledEntryDoesNotRunAfterTheResume() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithId(OncePerRun("B"), "2"), WithId(OncePerRun("C"), "3") });
    var (handle, _) = await PauseAfterFirstAsync(h);
    var disabledB = WithId(OncePerRun("B"), "2");
    disabledB.Enabled = false;

    ApplyAll(handle, TargetTemplate(WithId(OncePerRun("A"), "1"), disabledB, WithId(OncePerRun("C"), "3")));
    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "C");
  }

  [Fact]
  public async Task AnEntryThatWasDisabledAtTheStartRunsAfterItIsEnabled() {
    var h = new Harness();
    var disabledB = WithId(OncePerRun("B"), "2");
    disabledB.Enabled = false;
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), disabledB, WithId(OncePerRun("C"), "3") });
    var (handle, _) = await PauseAfterFirstAsync(h);

    ApplyAll(handle, TargetTemplate(WithId(OncePerRun("A"), "1"), WithId(OncePerRun("B"), "2"), WithId(OncePerRun("C"), "3")));
    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B", "C");
  }

  [Fact]
  public async Task ChangedParameterValuesReachTheNextFiringOfTheEntry() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithSlot(WithId(OncePerRun("B"), "2"), "old") });
    var (handle, _) = await PauseAfterFirstAsync(h);

    ApplyAll(handle, TargetTemplate(WithId(OncePerRun("A"), "1"), WithSlot(WithId(OncePerRun("B"), "2"), "new")));
    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B");
    h.Sequences.Scopes[1].Scope.TryResolve("slot", out var value).Should().BeTrue();
    value.Text.Should().Be("new");
  }

  [Fact]
  public async Task TheReplanIterationDoesNotCountACycleAndTheNextCycleStartsAgain() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithId(OncePerRun("B"), "2") }, cycle: true);
    var aStarted = new TaskCompletionSource();
    var releaseA = new TaskCompletionSource();
    var dStarted = new TaskCompletionSource();
    var releaseD = new TaskCompletionSource();
    var aFirst = true;
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A" && aFirst) {
        aFirst = false;
        aStarted.TrySetResult();
        await releaseA.Task.WaitAsync(ct);
      }
      else if (id == "D") {
        dStarted.TrySetResult();
        await releaseD.Task.WaitAsync(ct);
      }
      else {
        await Task.Delay(1, ct);
      }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var handle = await WaitForHandleAsync(h, "q1");
    handle.RequestOperatorPause(clock.GetLocalNow());
    releaseA.TrySetResult();
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);

    ApplyAll(handle, TargetTemplate(WithId(OncePerRun("A"), "1"), WithId(OncePerRun("D"), "4")));
    handle.ReleaseOperatorPause();
    await dStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

    handle.Cycles.SnapshotHealth().CyclesCompleted.Should().Be(0, "the first cycle is not complete: D still runs");
    h.Sequences.Executed.Take(2).Should().Equal("A", "D");

    releaseD.TrySetResult();
    await WaitForAsync(() => handle.Cycles.SnapshotHealth().CyclesCompleted >= 1);
    await h.Service.StopAsync("q1");

    handle.Cycles.SnapshotHealth().CyclesCompleted.Should().BeGreaterThan(0);
    h.Sequences.Executed.Should().NotContain("B", "B was removed");
  }

  [Fact]
  public async Task ATimerKeepsItsDueTimeAcrossAPauseAnApplyAndAResume() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithId(RelativeTimer("T", TimeSpan.FromMinutes(10)), "2") });
    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    var handle = await WaitForHandleAsync(h, "q1");
    var schedule = handle.Schedule!;
    var dueBefore = schedule.ComputeNextDue(handle, clock.GetLocalNow());
    handle.RequestOperatorPause(clock.GetLocalNow());
    for (var i = 0; i < 50 && handle.OperatorPause != OperatorPauseState.Paused; i++) {
      clock.Advance(TimeSpan.FromMilliseconds(300));
      await Task.Delay(20);
    }
    handle.OperatorPause.Should().Be(OperatorPauseState.Paused);

    // The apply changes the parameters of A. It does not touch the timer entry.
    ApplyAll(handle, TargetTemplate(WithSlot(WithId(OncePerRun("A"), "1"), "x"), WithId(RelativeTimer("T", TimeSpan.FromMinutes(10)), "2")));
    var dueAfterApply = schedule.ComputeNextDue(handle, clock.GetLocalNow());
    handle.ReleaseOperatorPause();
    await Task.Delay(100);
    h.Sequences.Executed.Should().Equal("A");

    clock.Advance(TimeSpan.FromMinutes(11));
    await WaitUntilStoppedAsync(h.Service, "q1");

    dueAfterApply.Should().Be(dueBefore);
    h.Sequences.Executed.Should().Equal("A", "T");
  }

  [Fact]
  public async Task AnEntryAddedAfterTheOnePerRunPassOfANonCyclingRunDoesNotRunInThisRun() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithId(RelativeTimer("T", TimeSpan.FromMinutes(10)), "2") });
    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    var handle = await WaitForHandleAsync(h, "q1");
    handle.RequestOperatorPause(clock.GetLocalNow());
    for (var i = 0; i < 50 && handle.OperatorPause != OperatorPauseState.Paused; i++) {
      clock.Advance(TimeSpan.FromMilliseconds(300));
      await Task.Delay(20);
    }

    ApplyAll(handle, TargetTemplate(WithId(OncePerRun("A"), "1"), WithId(RelativeTimer("T", TimeSpan.FromMinutes(10)), "2"), WithId(OncePerRun("E"), "5")));
    handle.ReleaseOperatorPause();
    clock.Advance(TimeSpan.FromMinutes(11));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "T");
  }

  [Fact]
  public async Task NoSwapMeansNoReplanAndTheRunGoesOnAsBefore() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "1"), WithId(OncePerRun("B"), "2") });
    var (handle, _) = await PauseAfterFirstAsync(h);

    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B");
    handle.Schedule!.Plan.Version.Should().Be(0);
  }
}
