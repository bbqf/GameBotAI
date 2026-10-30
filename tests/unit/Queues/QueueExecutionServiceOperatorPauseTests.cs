using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA2000

namespace GameBot.UnitTests.Queues;

// Feature 171: the operator pause. A pause request lets the running firing group (the scheduled sequence and its
// helper sequences) finish. Then the run loop waits at a gate and starts no new firing group until a resume.
public sealed partial class QueueExecutionServiceTests {
  private static async Task<QueueRunHandle> WaitForHandleAsync(Harness h, string queueId) {
    await WaitForAsync(() => h.Registry.TryGet(queueId, out _));
    h.Registry.TryGet(queueId, out var handle).Should().BeTrue();
    return handle;
  }

  [Fact]
  public async Task OperatorPauseLetsTheRunningGroupFinishAndStartsNoOtherGroup() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B"), OncePerRun("C") });
    var aStarted = new TaskCompletionSource();
    var releaseA = new TaskCompletionSource();
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A") {
        aStarted.TrySetResult();
        await releaseA.Task.WaitAsync(ct);
      }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var handle = await WaitForHandleAsync(h, "q1");
    handle.RequestOperatorPause(DateTimeOffset.UtcNow);
    handle.OperatorPause.Should().Be(OperatorPauseState.Pausing, "sequence A still runs");
    releaseA.TrySetResult();
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);
    await Task.Delay(150);

    handle.OperatorPause.Should().Be(OperatorPauseState.Paused);
    h.Sequences.Executed.Should().Equal("A");
    h.Service.IsRunning("q1").Should().BeTrue("a pause does not end the run");

    handle.ReleaseOperatorPause().Should().BeTrue();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B", "C");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task TheHelperSequencesOfTheRunningFiringFinishBeforeThePause() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), EveryStep("E"), OncePerRun("B") });
    var aStarted = new TaskCompletionSource();
    var releaseA = new TaskCompletionSource();
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A") {
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
    await Task.Delay(150);

    h.Sequences.Executed.Should().Equal("A", "E");

    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");
    h.Sequences.Executed.Should().Equal("A", "E", "B", "E");
  }

  [Fact]
  public async Task OperatorPauseStopsBetweenTwoDueTimerFirings() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { TimerEntry("T1", TimeOnly.MinValue), TimerEntry("T2", TimeOnly.MinValue), OncePerRun("A") });
    var t1Started = new TaskCompletionSource();
    var releaseT1 = new TaskCompletionSource();
    h.Sequences.Handler = async (id, ct) => {
      if (id == "T1") {
        t1Started.TrySetResult();
        await releaseT1.Task.WaitAsync(ct);
      }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await t1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var handle = await WaitForHandleAsync(h, "q1");
    handle.RequestOperatorPause(DateTimeOffset.UtcNow);
    releaseT1.TrySetResult();
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);
    await Task.Delay(150);

    h.Sequences.Executed.Should().Equal("T1");

    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");
    h.Sequences.Executed.Should().Equal("T1", "T2", "A");
  }

  [Fact]
  public async Task AnEntryThatIsDueDuringThePauseRunsAsLateAfterTheResume() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) });

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    var handle = await WaitForHandleAsync(h, "q1");
    var schedule = handle.Schedule!;
    var dueBefore = schedule.ComputeNextDue(handle, clock.GetLocalNow());
    handle.RequestOperatorPause(clock.GetLocalNow());

    // The run loop is idle in its poll delay: the pause takes effect at the next poll tick.
    for (var i = 0; i < 50 && handle.OperatorPause != OperatorPauseState.Paused; i++) {
      clock.Advance(TimeSpan.FromMilliseconds(300));
      await Task.Delay(20);
    }
    handle.OperatorPause.Should().Be(OperatorPauseState.Paused, "no group runs, so the pause is at once");

    clock.Advance(TimeSpan.FromMinutes(15));
    await Task.Delay(150);
    h.Sequences.Executed.Should().Equal("A");
    schedule.RelativeFired(1).Should().BeFalse("a pause never moves or skips a timer");

    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "T");
    dueBefore.Should().Be(schedule.RunStartedAt + TimeSpan.FromMinutes(10));
  }

  [Fact]
  public async Task APauseAloneChangesNoCyclePositionAndNoNextDueTime() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(30)) }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(5, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    var handle = await WaitForHandleAsync(h, "q1");
    handle.RequestOperatorPause(clock.GetLocalNow());
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);
    var executedBefore = h.Sequences.Executed.Count;
    var cyclesBefore = handle.Cycles.SnapshotHealth().CyclesCompleted;
    var dueBefore = handle.Schedule!.ComputeNextDue(handle, clock.GetLocalNow());
    var runStarted = handle.RunStartedAt;

    await Task.Delay(300);

    h.Sequences.Executed.Count.Should().Be(executedBefore);
    handle.Cycles.SnapshotHealth().CyclesCompleted.Should().Be(cyclesBefore);
    handle.Schedule!.ComputeNextDue(handle, clock.GetLocalNow()).Should().Be(dueBefore);
    handle.RunStartedAt.Should().Be(runStarted);
    h.Service.IsRunning("q1").Should().BeTrue();

    handle.ReleaseOperatorPause();
    await WaitForAsync(() => h.Sequences.Executed.Count > executedBefore);
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task AFailedRunningSequenceStillLetsThePauseStartAfterIt() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B") });
    var aStarted = new TaskCompletionSource();
    var releaseA = new TaskCompletionSource();
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A") {
        aStarted.TrySetResult();
        await releaseA.Task.WaitAsync(ct);
        return FakeSequenceExecution.Failure(id);
      }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var handle = await WaitForHandleAsync(h, "q1");
    handle.RequestOperatorPause(DateTimeOffset.UtcNow);
    releaseA.TrySetResult();
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);
    await Task.Delay(100);

    h.Sequences.Executed.Should().Equal("A");

    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");
    h.Sequences.Executed.Should().Equal("A", "B");
  }

  [Fact]
  public async Task AStopRequestEndsAPausedRunAndClearsThePause() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B") });
    var aStarted = new TaskCompletionSource();
    var releaseA = new TaskCompletionSource();
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A") {
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

    await h.Service.StopAsync("q1");

    h.Service.IsRunning("q1").Should().BeFalse();
    handle.OperatorPause.Should().Be(OperatorPauseState.None, "the pause belongs to the run (FR-007)");
    h.Sequences.Executed.Should().Equal("A");
    h.Log.Summary.Should().Contain("stopped manually");
  }

  [Fact]
  public async Task APauseRequestDuringAnIdleHoldPausesAtOnceAndLeavesTheGameBackedOut() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)) }, pauseWhenIdle: true, idleThresholdSeconds: 30);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sessions.HomeCount >= 1, 10000);
    var handle = await WaitForHandleAsync(h, "q1");
    handle.IsIdlePaused.Should().BeTrue();
    var foregroundCallsBefore = h.EnsureGame.Calls;
    handle.RequestOperatorPause(clock.GetLocalNow());

    for (var i = 0; i < 50 && handle.OperatorPause != OperatorPauseState.Paused; i++) {
      clock.Advance(TimeSpan.FromMilliseconds(300));
      await Task.Delay(20);
    }

    handle.OperatorPause.Should().Be(OperatorPauseState.Paused, "an idle hold is not a firing group");
    handle.IsIdlePaused.Should().BeFalse("the hold stopped early");
    h.EnsureGame.Calls.Should().Be(foregroundCallsBefore, "the game stays backed out while the queue is paused");
    handle.GameBackedOut.Should().BeTrue();
    h.Sequences.Executed.Should().Equal("A");

    handle.ReleaseOperatorPause();
    await WaitForAsync(() => h.EnsureGame.Calls > foregroundCallsBefore);

    h.EnsureGame.Calls.Should().Be(foregroundCallsBefore + 1, "the resume brings the game to the front");
    handle.GameBackedOut.Should().BeFalse();

    clock.Advance(TimeSpan.FromMinutes(11));
    await WaitUntilStoppedAsync(h.Service, "q1", 10000);
    h.Sequences.Executed.Should().Equal("A", "T");
  }

  [Fact]
  public async Task AFreshRunAfterAPausedRunIsNotPaused() {
    // A service restart ends the run, and ResumeOnServiceStart then starts a fresh run with StartAsync. The pause
    // belongs to the run handle, so the fresh run starts with no pause (FR-007, SC-007).
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B") });
    var aStarted = new TaskCompletionSource();
    var releaseA = new TaskCompletionSource();
    var firstRun = true;
    h.Sequences.Handler = async (id, ct) => {
      if (id == "A" && firstRun) {
        aStarted.TrySetResult();
        await releaseA.Task.WaitAsync(ct);
      }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var first = await WaitForHandleAsync(h, "q1");
    first.RequestOperatorPause(DateTimeOffset.UtcNow);
    releaseA.TrySetResult();
    await WaitForAsync(() => first.OperatorPause == OperatorPauseState.Paused);
    await h.Service.StopAsync("q1");
    firstRun = false;
    h.Sequences.Executed.Clear();

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task ARunWithNoPauseRequestRunsAsBefore() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task AnOperatorPauseDoesNotChangeAPolicyPause() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), OncePerRun("B") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(5, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    var handle = await WaitForHandleAsync(h, "q1");
    handle.EnterPolicyPause("failure policy: test", DateTimeOffset.UtcNow);
    handle.RequestOperatorPause(DateTimeOffset.UtcNow).Should().BeTrue("a request is accepted during a policy pause");
    await Task.Delay(100);

    handle.IsPolicyPaused.Should().BeTrue();
    handle.SnapshotPause().Kind.Should().Be(QueuePauseKinds.FailurePolicy);

    handle.ReleaseOperatorPause().Should().BeTrue();
    handle.ResumeFromPolicyPause().Should().BeTrue();
    await h.Service.StopAsync("q1");
  }
}
