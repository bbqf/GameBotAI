using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA2000

namespace GameBot.UnitTests.Queues;

// Feature 184: the run-next register and its drain in the run loop. A non-cycling run with a far relative timer stays
// alive, so the tests can call RunNext after the other entries ended.
public sealed partial class QueueExecutionServiceTests {
  private static QueueTemplateEntry FarTimer() => WithId(RelativeTimer("Far", TimeSpan.FromHours(1)), "e-far");

  private static string StateOf(Harness h, QueueRunHandle handle, string entryId) =>
    QueueEntryStateReader.ReadAll(handle, handle.Schedule!, h.Clock!.GetLocalNow()).GetValueOrDefault(entryId, "none");

  /// <summary>Starts the queue and waits until the entry is idle (its sequence ran and has no pending firing).</summary>
  private static async Task<QueueRunHandle> StartAndWaitIdleAsync(Harness h, string entryId, string sequenceId) {
    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains(sequenceId));
    var handle = await WaitForHandleAsync(h, "q1");
    await WaitForAsync(() => handle.Schedule is not null && StateOf(h, handle, entryId) == "idle");
    return handle;
  }

  [Fact]
  public async Task RunNextRunsAnIdleEntryOnceWithItsParameterValues() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithSlot(WithId(OncePerRun("A"), "e-a"), "x"), FarTimer() });
    await StartAndWaitIdleAsync(h, "e-a", "A");

    var result = h.Service.RunNext("q1", "e-a");

    result.Outcome.Should().Be(RunNextOutcome.Queued);
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    await Task.Delay(150);
    h.Sequences.Executed.Count(id => id == "A").Should().Be(2);
    h.Sequences.Scopes[1].Scope.TryResolve("slot", out var value).Should().BeTrue();
    value.Text.Should().Be("x");
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task RunNextDoesNotChangeTheOtherEntries() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), WithId(OncePerRun("B"), "e-b"), FarTimer() });
    var handle = await StartAndWaitIdleAsync(h, "e-a", "A");
    await WaitForAsync(() => StateOf(h, handle, "e-b") == "idle");

    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);

    h.Sequences.Executed.Count(id => id == "B").Should().Be(1);
    h.Sequences.Executed.Should().NotContain("Far");
    StateOf(h, handle, "e-far").Should().Be("scheduled");
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task ASecondRequestForAQueuedEntryAddsNoSecondRun() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), FarTimer() });
    var handle = await StartAndWaitIdleAsync(h, "e-a", "A");
    handle.RequestOperatorPause(DateTimeOffset.UtcNow);
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);

    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.AlreadyQueued);
    StateOf(h, handle, "e-a").Should().Be("queued");

    handle.ReleaseOperatorPause();
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    await Task.Delay(150);
    h.Sequences.Executed.Count(id => id == "A").Should().Be(2);
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task ARequestMadeInAnOperatorPauseFiresOnlyAfterTheResume() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), FarTimer() });
    var handle = await StartAndWaitIdleAsync(h, "e-a", "A");
    handle.RequestOperatorPause(DateTimeOffset.UtcNow);
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);

    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
    await Task.Delay(250);
    h.Sequences.Executed.Count(id => id == "A").Should().Be(1, "a paused queue starts no sequence");

    handle.ReleaseOperatorPause();
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task ARequestMadeInAFailurePolicyPauseFiresOnlyAfterTheResume() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), FarTimer() });
    var handle = await StartAndWaitIdleAsync(h, "e-a", "A");
    handle.EnterPolicyPause("test pause", DateTimeOffset.UtcNow);

    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
    await Task.Delay(250);
    h.Sequences.Executed.Count(id => id == "A").Should().Be(1, "a parked run fires nothing");

    handle.ResumeFromPolicyPause().Should().BeTrue();
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task ARequestFiresRightAfterTheRunningSequenceNotAfterTheWholePass() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] {
      WithId(AtQueueStart("S"), "e-s"), WithId(OncePerRun("A"), "e-a"), WithId(OncePerRun("B"), "e-b"), WithId(OncePerRun("C"), "e-c"), FarTimer() });
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

    // S ran once at the start, so it is idle while A runs.
    h.Service.RunNext("q1", "e-s").Outcome.Should().Be(RunNextOutcome.Queued);
    releaseA.TrySetResult();
    await WaitForAsync(() => h.Sequences.Executed.Contains("C"));

    h.Sequences.Executed.Should().Equal("S", "A", "S", "B", "C");
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task ANonCyclingRunEndsAfterTheRequestFires() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), WithId(OncePerRun("B"), "e-b") });
    var (handle, _) = await PauseAfterFirstAsync(h);
    await WaitForAsync(() => handle.Schedule!.Plan.Keys.Count == 2);

    // A is done and idle. The loop waits at the gate before B. The request runs first after the resume.
    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
    handle.ReleaseOperatorPause();
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "A", "B");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task AFailedRunOfTheEntryDoesNotStopTheQueueAndTheEntryCanBeQueuedAgain() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), FarTimer() });
    var runs = 0;
    h.Sequences.Handler = (id, ct) => Task.FromResult(
      id == "A" && Interlocked.Increment(ref runs) == 2 ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));
    var handle = await StartAndWaitIdleAsync(h, "e-a", "A");

    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    await WaitForAsync(() => StateOf(h, handle, "e-a") == "idle");

    h.Service.IsRunning("q1").Should().BeTrue();
    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 3);
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task AStopClearsTheRegisterAndANewRunHasNoRequest() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), FarTimer() });
    var first = await StartAndWaitIdleAsync(h, "e-a", "A");
    first.RequestOperatorPause(DateTimeOffset.UtcNow);
    await WaitForAsync(() => first.OperatorPause == OperatorPauseState.Paused);
    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);

    await h.Service.StopAsync("q1");
    first.HasPendingRunNext.Should().BeFalse();

    await h.Service.StartAsync("q1");
    var second = await WaitForHandleAsync(h, "q1");
    second.Should().NotBeSameAs(first);
    second.HasPendingRunNext.Should().BeFalse();
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    await Task.Delay(150);
    h.Sequences.Executed.Count(id => id == "A").Should().Be(2, "one run for each start, and none for the old request");
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task ATemplateEditThatIsNotAppliedDoesNotChangeTheEntryThatRuns() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), FarTimer() });
    await StartAndWaitIdleAsync(h, "e-a", "A");
    var edited = new QueueTemplate { Id = "tpl-q1", Name = "T-q1" };
    edited.Entries.Add(WithId(OncePerRun("Z"), "e-a"));
    h.Templates.Add(edited);

    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);

    h.Sequences.Executed.Should().NotContain("Z");
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task RunNextRefusesAnEntryThatIsScheduledRunningOrUnknown() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), WithId(OncePerRun("B"), "e-b"), FarTimer() });
    var bStarted = new TaskCompletionSource();
    var releaseB = new TaskCompletionSource();
    h.Sequences.Handler = async (id, ct) => {
      if (id == "B") {
        bStarted.TrySetResult();
        await releaseB.Task.WaitAsync(ct);
      }
      return FakeSequenceExecution.Success(id);
    };
    await h.Service.StartAsync("q1");
    await bStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

    h.Service.RunNext("q1", "e-b").Outcome.Should().Be(RunNextOutcome.EntryRunning);
    h.Service.RunNext("q1", "e-far").Outcome.Should().Be(RunNextOutcome.EntryScheduled);
    h.Service.RunNext("q1", "missing").Outcome.Should().Be(RunNextOutcome.EntryNotAvailable);
    h.Service.RunNext("q1", "idx:0").Outcome.Should().Be(RunNextOutcome.EntryNotAvailable);
    releaseB.TrySetResult();
    await h.Service.StopAsync("q1");
    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.NotRunning);
  }

  [Fact]
  public async Task RunNextRefusesADisabledEntry() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    var disabled = WithId(OncePerRun("D"), "e-d");
    disabled.Enabled = false;
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), disabled, FarTimer() });
    await StartAndWaitIdleAsync(h, "e-a", "A");

    h.Service.RunNext("q1", "e-d").Outcome.Should().Be(RunNextOutcome.EntryNotAvailable);
    await h.Service.StopAsync("q1");
  }

  // Perf goal of the plan (Principle IV): a call returns in 100 ms or less at p95. The call reads the schedule and one register
  // under short locks, so the real figure is far lower. The loop adds one empty-list check for each firing group.
  [Fact]
  public async Task TheRunNextCallReturnsInUnder100MillisecondsAtP95() {
    var h = new Harness(new FakeTimeProvider(FakeStart));
    AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), FarTimer() });
    var handle = await StartAndWaitIdleAsync(h, "e-a", "A");
    handle.RequestOperatorPause(DateTimeOffset.UtcNow);
    await WaitForAsync(() => handle.OperatorPause == OperatorPauseState.Paused);
    h.Service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);

    var millis = new double[1000];
    for (var i = 0; i < millis.Length; i++) {
      var start = System.Diagnostics.Stopwatch.GetTimestamp();
      h.Service.RunNext("q1", "e-a");
      millis[i] = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    Array.Sort(millis);

    millis[(int)(millis.Length * 0.95)].Should().BeLessThan(100);
    await h.Service.StopAsync("q1");
  }}
