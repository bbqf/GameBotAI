using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.QueueTemplates;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

// Feature 095 (#202), changed by feature 188 (#393): BeforeEachRun entries run in template order immediately
// before each timed, live-scheduled, or self-rescheduled firing, also when several firings are due in one wake-up.
public sealed partial class QueueExecutionServiceTests {
  private static QueueTemplateEntry BeforeEachRun(string id, bool enabled = true)
    => new() { SequenceId = id, ScheduleType = ScheduleType.BeforeEachRun, Enabled = enabled };

  // ── US1: runs before each triggering firing ────────────────────────────────

  [Fact] // T004(a)
  public async Task BeforeEachRunRunsImmediatelyBeforeTimeOfDayTimer() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B"), TimerEntry("T", TimeOnly.MinValue), OncePerRun("A") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // The once-per-run step A is not a triggering firing, so B does not run before it.
    h.Sequences.Executed.Should().Equal("B", "T", "A");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact] // T004(b)
  public async Task BeforeEachRunRunsBeforeRelativeTimer() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { RelativeTimer("T", TimeSpan.Zero), OncePerRun("A"), BeforeEachRun("B") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B", "T", "A");
  }

  [Fact] // T004(c)
  public async Task BeforeEachRunRunsBeforeLiveSchedule() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), BeforeEachRun("B") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(10, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Service.IsRunning("q1"));
    h.Service.ScheduleRelative("q1", "L", TimeSpan.FromMinutes(5));
    await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
    h.Sequences.Executed.Should().NotContain("B");

    clock.Advance(TimeSpan.FromMinutes(5));
    await WaitForAsync(() => h.Sequences.Executed.Contains("L"));
    await h.Service.StopAsync("q1");

    var executed = h.Sequences.Executed.ToList();
    executed.Count(id => id == "B").Should().Be(1);
    executed[executed.IndexOf("L") - 1].Should().Be("B");
  }

  [Fact] // T004(d)
  public async Task BeforeEachRunRunsBeforeSelfRescheduleTimerFiring() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), BeforeEachRun("B") });
    var scheduled = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref scheduled, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Registry.TryGet("q1", out var handle) && handle.HasPendingTimerFirings, 10000);
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B", "R");
    h.Log.Summary.Should().Contain("2 sequence(s) executed"); // A + R; B does not count
  }

  [Fact] // T004(e)
  public async Task MultipleBeforeEachRunEntriesRunInTemplateOrder() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B1"), TimerEntry("T", TimeOnly.MinValue), BeforeEachRun("B2") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B1", "B2", "T");
  }

  [Fact] // T004(f)
  public async Task BeforeEachRunFailureIsNonFatalAndTheTriggeringFiringStillRuns() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B"), RelativeTimer("T", TimeSpan.Zero), OncePerRun("A") });
    h.Sequences.Handler = (id, ct) =>
      Task.FromResult(id == "B" ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B", "T", "A");
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("2 sequence(s) executed"); // T (relative) + A
    h.Log.Summary.Should().Contain("1 failed");
  }

  [Fact] // T004(g)
  public async Task DisabledBeforeEachRunEntryNeverRuns() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B", enabled: false), TimerEntry("T", TimeOnly.MinValue), OncePerRun("A") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("T", "A");
  }

  [Fact] // T004(h)
  public async Task BeforeEachRunRunsBeforeDailyRetryOfFailedTimer() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock, config: RetryConfig(maxAttempts: 3));
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B"), TimerEntry("T", TimeOnly.FromDateTime(FakeStart.DateTime)), OncePerRun("A") });
    var attempts = 0;
    h.Sequences.Handler = (id, ct) => Task.FromResult(
      id == "T" && Interlocked.Increment(ref attempts) == 1
        ? FakeSequenceExecution.Failure(id)
        : FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(100);
    h.Sequences.Executed.Should().Equal("B", "T", "A");

    clock.Advance(TimeSpan.FromMinutes(30));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B", "T", "A", "B", "T");
  }

  [Fact] // T004(i)
  public async Task BeforeEachRunRunsBeforeSelfRescheduleOncePerRunFiring() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), BeforeEachRun("B") });
    var scheduled = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref scheduled, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.OncePerRun, null, null);
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B", "R");
  }

  [Fact] // T004(i)
  public async Task BeforeEachRunRunsBeforeSelfRescheduleAtQueueStartFiring() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), BeforeEachRun("B") }, cycle: true);
    var scheduled = 0;
    h.Sequences.Handler = async (id, ct) => {
      await Task.Delay(10, ct);
      if (id == "A" && Interlocked.Exchange(ref scheduled, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.AtQueueStart, null, null);
      }
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("R"));
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Take(3).Should().Equal("A", "B", "R");
    h.Sequences.Executed.Count(id => id == "B").Should().Be(1);
  }

  [Fact] // T004(j) — US1-AS5
  public async Task BeforeEachRunRunsAfterIdlePauseEndsAndBeforeTheDueFiring() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    h.EnsureGame.ExecutedCountProvider = () => h.Sequences.Executed.Count;
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)), BeforeEachRun("B") }, pauseWhenIdle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sessions.HomeCount >= 1);
    h.Sequences.Executed.Should().Equal("A");

    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.EnsureGame.ExecutedCountAtFirstCall.Should().Be(1); // game foregrounded before B
    h.Sequences.Executed.Should().Equal("A", "B", "T");
  }

  [Fact] // T004(k) — FR-015
  public async Task BeforeEachRunIsLoggedUnderTheRunRootAndRecordedInTheCycleLedger() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B"), RelativeTimer("T", TimeSpan.Zero) }, cycle: true);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => CycleHealth(h).CyclesCompleted >= 1, 10000);

    h.Registry.TryGet("q1", out var handle).Should().BeTrue();
    var cycle = handle.Cycles.SnapshotCycles(10).Single();
    // Template timer firings have never been ledger entries; the pass records its own execution.
    cycle.Entries.Select(e => e.SequenceId).Should().Equal("B");

    await h.Service.StopAsync("q1");

    var rootOfB = h.Sequences.RootIds[h.Sequences.Executed.IndexOf("B")];
    var rootOfT = h.Sequences.RootIds[h.Sequences.Executed.IndexOf("T")];
    rootOfB.Should().NotBeNull();
    rootOfB.Should().Be(rootOfT);
  }

  // ── Feature 188 (#393): one pass before each triggering firing ──

  [Fact] // T008(a)/(e), changed by feature 188
  public async Task BeforeEachRunRunsBeforeEachCoDueFiringAndTriggersNoEveryStepPass() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      BeforeEachRun("B"), TimerEntry("T1", TimeOnly.MinValue), TimerEntry("T2", TimeOnly.MinValue), EveryStep("E")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // B runs immediately before each firing. No every-step pass runs after B.
    h.Sequences.Executed.Should().Equal("B", "T1", "E", "B", "T2", "E");
  }

  [Fact] // Feature 188: US1-AS1, SC-001
  public async Task BeforeEachRunRunsBeforeEachOfThreeCoDueTimers() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      BeforeEachRun("B"), TimerEntry("T1", TimeOnly.MinValue), TimerEntry("T2", TimeOnly.MinValue), TimerEntry("T3", TimeOnly.MinValue)
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B", "T1", "B", "T2", "B", "T3");
  }

  [Fact] // Feature 188: US1-AS2, FR-003
  public async Task SeveralBeforeEachRunEntriesRunInTemplateOrderBeforeEachCoDueFiring() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      BeforeEachRun("B1"), TimerEntry("T1", TimeOnly.MinValue), BeforeEachRun("B2"), TimerEntry("T2", TimeOnly.MinValue)
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B1", "B2", "T1", "B1", "B2", "T2");
  }

  [Fact] // Feature 188: US1-AS5, FR-002
  public async Task BeforeEachRunRunsBeforeEachFiringOfMixedKindsInOneWakeUp() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      BeforeEachRun("B"), TimerEntry("T", TimeOnly.MinValue), RelativeTimer("R", TimeSpan.Zero), OncePerRun("A")
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    // The template once-per-run step A gets no pass.
    h.Sequences.Executed.Should().Equal("B", "T", "B", "R", "A");
  }

  [Fact] // Feature 188: the next wake-up after an idle gap also runs one pass for each co-due firing
  public async Task TwoCoDueRelativeTimersAfterAnIdleGapEachGetAPass() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] {
      OncePerRun("A"), RelativeTimer("T1", TimeSpan.FromMinutes(10)), RelativeTimer("T2", TimeSpan.FromMinutes(10)), BeforeEachRun("B")
    });

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(100);
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "B", "T1", "B", "T2");
  }

  [Fact] // Feature 188: US1-AS5, FR-002
  public async Task BeforeEachRunRunsBeforeCoDueRelativeLiveAndSelfRescheduledFirings() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)), BeforeEachRun("B") });
    var scheduled = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "A" && Interlocked.Exchange(ref scheduled, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    h.Service.ScheduleRelative("q1", "L", TimeSpan.FromMinutes(10));
    await Task.Delay(100);
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitForAsync(() => h.Sequences.Executed.Contains("T") && h.Sequences.Executed.Contains("L") && h.Sequences.Executed.Contains("R"), 10000);
    await h.Service.StopAsync("q1");

    var executed = h.Sequences.Executed.ToList();
    executed.Count(id => id == "B").Should().Be(3);
    foreach (var firing in new[] { "T", "L", "R" }) {
      executed[executed.IndexOf(firing) - 1].Should().Be("B", "a pass runs immediately before {0}", firing);
    }
  }

  // ── Feature 188 (#393): accounting and run log for each pass ──

  [Fact] // Feature 188: US2-AS1, US2-AS2, FR-005
  public async Task BeforeEachRunFailureIsCountedForEachPassAndBothFiringsRun() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      BeforeEachRun("B"), RelativeTimer("T1", TimeSpan.Zero), RelativeTimer("T2", TimeSpan.Zero), OncePerRun("A")
    });
    h.Sequences.Handler = (id, ct) =>
      Task.FromResult(id == "B" ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B", "T1", "B", "T2", "A");
    h.Log.FinalStatus.Should().Be("success");
    h.Log.Summary.Should().Contain("3 sequence(s) executed"); // T1 + T2 + A; B does not count
    h.Log.Summary.Should().Contain("2 failed");
  }

  [Fact] // Feature 188: US2-AS3, FR-006
  public async Task EachBeforeEachRunPassIsItsOwnLogNodeUnderTheRunRoot() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] {
      BeforeEachRun("B"), TimerEntry("T1", TimeOnly.MinValue), TimerEntry("T2", TimeOnly.MinValue)
    });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B", "T1", "B", "T2");
    var rootOfT1 = h.Sequences.RootIds[h.Sequences.Executed.IndexOf("T1")];
    foreach (var index in new[] { 0, 2 }) {
      h.Sequences.Executed[index].Should().Be("B");
      h.Sequences.RootIds[index].Should().NotBeNull();
      h.Sequences.RootIds[index].Should().Be(rootOfT1);
    }
  }

  // ── No triggering firing, stop, and session re-bind ──

  [Fact] // T008(b)
  public async Task BeforeEachRunDoesNotRunWithoutATriggeringFiring() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B"), OncePerRun("A"), OncePerRun("A2") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A", "A2");
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact] // T008(c)
  public async Task BeforeEachRunOnlyTemplateBehavesAsEmpty() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B") });

    await h.Service.StartAsync("q1");
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Service.IsRunning("q1").Should().BeFalse();
    h.Sequences.Executed.Should().BeEmpty();
    h.Log.FinalStatus.Should().Be("success");
  }

  [Fact] // T008(d)
  public async Task BeforeEachRunNeverRunsAcrossCyclesOfOncePerRunSteps() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), BeforeEachRun("B") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(5, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 3);
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Should().NotContain("B");
  }

  [Fact] // T008(f)
  public async Task StopDuringBeforeEachRunSkipsTheTriggeringFiring() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B"), TimerEntry("T", TimeOnly.MinValue), OncePerRun("A") });
    h.Sequences.Handler = async (id, ct) => {
      if (id == "B") await Task.Delay(Timeout.Infinite, ct);
      return FakeSequenceExecution.Success(id);
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("B"));
    await h.Service.StopAsync("q1");

    h.Sequences.Executed.Should().Equal("B");
    h.Log.Summary.Should().Contain("stopped manually");
  }

  [Fact] // T008(g)
  public async Task ConnectionLostBeforeBeforeEachRunFailsTheRun() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)), BeforeEachRun("B") });
    h.Sequences.Handler = (id, ct) => {
      // The device is genuinely gone, so the #217 re-bind cannot recover the session.
      if (id == "A") {
        h.Sessions.CreateThrows = new System.Collections.Generic.KeyNotFoundException("ADB device 'emu-1' not found");
        h.Sessions.Connected = false;
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(100);
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("A");
    h.Log.FinalStatus.Should().Be("failure");
  }

  [Fact] // #217: the reported failure — the session vanished during the idle gap, the device did not
  public async Task SessionEvictedDuringIdleIsReboundBeforeBeforeEachRun() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    // T2 keeps the non-cycling run alive after T, so "still running" is observable.
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T", TimeSpan.FromMinutes(10)), RelativeTimer("T2", TimeSpan.FromMinutes(60)), BeforeEachRun("B") });

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(100);
    h.Sessions.Evict(h.Sessions.SingleSessionId());
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitForAsync(() => h.Sequences.Executed.Contains("T"));

    h.Sequences.Executed.Should().Equal("A", "B", "T");
    h.Sessions.Created.Should().Be(2);
    h.Service.IsRunning("q1").Should().BeTrue();

    await h.Service.StopAsync("q1");
    h.Log.Summary.Should().Contain("stopped manually");
  }

  [Fact] // #217: several firings due at one wake-up share a single re-bind
  public async Task SeveralFiringsDueAtOneWakeUpRebindOnce() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A"), RelativeTimer("T1", TimeSpan.FromMinutes(10)), RelativeTimer("T2", TimeSpan.FromMinutes(10)), RelativeTimer("T3", TimeSpan.FromMinutes(60)), BeforeEachRun("B") });

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(100);
    h.Sessions.Evict(h.Sessions.SingleSessionId());
    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitForAsync(() => h.Sequences.Executed.Contains("T1") && h.Sequences.Executed.Contains("T2"));

    h.Sessions.Created.Should().Be(2);

    await h.Service.StopAsync("q1");
  }

  [Fact] // T008(h)
  public async Task BeforeEachRunSelfRescheduleBookingFiresLaterWithItsOwnPass() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { BeforeEachRun("B"), RelativeTimer("T", TimeSpan.Zero), OncePerRun("A") });
    var booked = 0;
    h.Sequences.Handler = (id, ct) => {
      if (id == "B" && Interlocked.Exchange(ref booked, 1) == 0) {
        h.Coordinator.ScheduleSelf("q1", "R", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10));
      }
      return Task.FromResult(FakeSequenceExecution.Success(id));
    };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
    await Task.Delay(100);
    h.Sequences.Executed.Should().Equal("B", "T", "A");

    clock.Advance(TimeSpan.FromMinutes(10));
    await WaitUntilStoppedAsync(h.Service, "q1");

    h.Sequences.Executed.Should().Equal("B", "T", "A", "B", "R");
  }
}
