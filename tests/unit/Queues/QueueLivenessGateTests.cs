using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1034 // CA1034: nested so that the tests can use the queue harness

namespace GameBot.UnitTests.Queues;

public sealed partial class QueueExecutionServiceTests {
  /// <summary>
  /// Feature 106 (FR-016, SC-008, research R-011): the liveness gate before each firing group. A hard
  /// reason holds the firing: it does not run, it stays due, and only the first held firing of each
  /// sequence in the episode writes a log entry. Nested here, so that it can use the queue harness.
  /// </summary>
  public sealed class QueueLivenessGateTests {
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static QueueTemplateEntry Timer(string id, int hour) =>
      new() { SequenceId = id, ScheduleType = ScheduleType.Timer, TimerTimeOfDay = new TimeOnly(hour, 0) };
    private static QueueTemplateEntry Every(string id) => new() { SequenceId = id, ScheduleType = ScheduleType.EveryStep };
    private static QueueTemplateEntry Before(string id) => new() { SequenceId = id, ScheduleType = ScheduleType.BeforeEachRun };
    private static QueueTemplateEntry Once(string id) => new() { SequenceId = id, ScheduleType = ScheduleType.OncePerRun };
    private static QueueTemplateEntry AtStart(string id) => new() { SequenceId = id, ScheduleType = ScheduleType.AtQueueStart };

    private sealed class Rig {
      public Harness H { get; }
      public FakeSessionLivenessService Liveness { get; } = new();
      public InMemoryRunStatisticsStore Stats { get; } = new();
      public QueueExecutionService Service { get; }
      public FakeTimeProvider Clock { get; }

      public Rig(GameBot.Domain.Config.AppConfig? config = null) {
        Clock = new FakeTimeProvider(Noon);
        H = new Harness(Clock);
        Service = new QueueExecutionService(H.Queues, H.Runtime, H.Templates, H.Sequences, H.Sessions, H.Log,
          NullLogger<QueueExecutionService>.Instance, H.Registry, timeProvider: Clock, config: config,
          runStatistics: Stats, liveness: Liveness);
      }

      public QueueRunHandle Handle(string id) => H.Registry.TryGet(id, out var handle) ? handle : throw new InvalidOperationException("not running");

      public List<string> Executed() { lock (H.Sequences.Executed) return H.Sequences.Executed.ToList(); }

      public List<(string SequenceId, string FinalStatus, string Summary, GameBot.Service.Services.ExecutionLog.ExecutionLogContext Context)> Entries() {
        lock (H.Log.SequenceEntries) return H.Log.SequenceEntries.ToList();
      }

      /// <summary>Waits until the gate evaluated the device <paramref name="more"/> more times.</summary>
      public Task PassIterationsAsync(int more = 5) {
        var target = Liveness.Evaluations + more;
        return WaitForAsync(() => Liveness.Evaluations >= target, 5000);
      }

      public async Task StopAsync(string id) {
        await Service.StopAsync(id);
        await WaitUntilStoppedAsync(Service, id);
      }
    }

    [Fact]
    public async Task AHardReasonHoldsTheFiringAndWritesOneEntryAndOneStatisticsFailure() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Once("A") }, cycle: true);
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(8);

      var handle = rig.Handle("q1");
      rig.Executed().Should().BeEmpty("a held firing does not run");
      var entry = rig.Entries().Should().ContainSingle("a second held firing of A writes no entry").Subject;
      entry.SequenceId.Should().Be("A");
      entry.FinalStatus.Should().Be("failure");
      entry.Summary.Should().Be("device_not_live: capture_stalled");
      entry.Context.Depth.Should().Be(1);
      entry.Context.RootExecutionId.Should().Be(handle.RootExecutionId);
      entry.Context.ParentExecutionId.Should().Be(handle.RootExecutionId);
      rig.Stats.Records.Should().ContainSingle(r => r.SequenceId == "A" && r.Record.Status == SequenceRunStatus.Failure);
      handle.Liveness.Snapshot().GatedFirings.Should().BeGreaterThanOrEqualTo(2);
      handle.Cycles.SnapshotHealth().CyclesCompleted.Should().Be(0, "a held iteration completes no cycle");
      await rig.StopAsync("q1");
    }

    [Fact]
    public async Task TheLoopWaitsTheCheckIntervalAfterAHeldFiring() {
      var rig = new Rig();
      rig.Liveness.Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 200, QueueGracePeriodMs = 120000 };
      AddQueueWithEntries(rig.H, "q1", new[] { Once("A") }, cycle: true);
      rig.Liveness.SetNotLive(DeviceLivenessReasons.InputTimeout);

      await rig.Service.StartAsync("q1");
      await Task.Delay(700);

      // About 700 / 200 checks by the loop plus as many by the watch; a loop with no wait makes thousands.
      rig.Liveness.Evaluations.Should().BeLessThan(20);
      await rig.StopAsync("q1");
    }

    [Fact]
    public async Task OneGateForEachFiringGroupAndNoGuardSequenceRuns() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Every("E"), Before("B"), Timer("T", 11) });
      rig.Liveness.SetNotLive(DeviceLivenessReasons.TransportNotReady);

      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(6);

      rig.Executed().Should().BeEmpty("the guard sequences of a held group do not run");
      rig.Entries().Select(e => e.SequenceId).Should().Equal("T");
      await rig.StopAsync("q1");
    }

    [Fact]
    public async Task AStandaloneEveryStepPassGivesOneEntryForItsFirstSequence() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Every("E1"), Every("E2") }, cycle: true);
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(6);

      rig.Executed().Should().BeEmpty();
      rig.Entries().Select(e => e.SequenceId).Should().Equal("E1");
      await rig.StopAsync("q1");
    }

    [Fact]
    public async Task AHeldTimeOfDayTimerStaysDueAndRunsAfterARecovery() {
      var rig = new Rig(new GameBot.Domain.Config.AppConfig { QueueDailyRetryMaxAttempts = 3 });
      AddQueueWithEntries(rig.H, "q1", new[] { Timer("T", 11) });
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(6);

      var schedule = rig.Handle("q1").Schedule!;
      schedule.TimeOfDayFiredOn(0, DateOnly.FromDateTime(Noon.DateTime)).Should().BeFalse("a held timer is not marked fired");
      schedule.HasPendingDailyRetries.Should().BeFalse("a hold arms no daily retry");

      rig.Liveness.SetLive();
      await WaitUntilStoppedAsync(rig.Service, "q1");

      rig.Executed().Should().Equal("T");
    }

    [Fact]
    public async Task AHeldDailyRetryKeepsItsAttemptNumber() {
      var rig = new Rig(new GameBot.Domain.Config.AppConfig { QueueDailyRetryMaxAttempts = 3, QueueDailyRetryDelayMs = 60000 });
      AddQueueWithEntries(rig.H, "q1", new[] { Timer("T", 11) });
      var runs = 0;
      rig.H.Sequences.Handler = (id, ct) =>
        Task.FromResult(Interlocked.Increment(ref runs) == 1 ? FakeSequenceExecution.Failure(id) : FakeSequenceExecution.Success(id));

      await rig.Service.StartAsync("q1");
      await WaitForAsync(() => rig.H.Registry.TryGet("q1", out var hd) && hd.Schedule?.HasPendingDailyRetries == true);
      var schedule = rig.Handle("q1").Schedule!;
      rig.Liveness.SetNotLive(DeviceLivenessReasons.InputTimeout);
      rig.Clock.Advance(TimeSpan.FromMinutes(2));
      await WaitForAsync(() => rig.Entries().Count > 0);
      await rig.PassIterationsAsync(6);

      schedule.DueDailyRetries(rig.Clock.GetLocalNow()).Should().Equal(new[] { (0, 1) }, "the held retry keeps attempt 1");
      rig.Executed().Should().Equal("T");

      rig.Liveness.SetLive();
      await WaitUntilStoppedAsync(rig.Service, "q1");
      rig.Executed().Should().Equal("T", "T");
    }

    [Fact]
    public async Task AHeldSelfRescheduleTimerFiringKeepsItsFireAt() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Once("A") }, cycle: true);
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(2);
      var fireAt = Noon.AddMinutes(-1);
      rig.Handle("q1").AddTimerFiring(new SelfRescheduleEntry("sr-1", "S", SelfRescheduleOption.Timer, fireAt));

      await rig.PassIterationsAsync(6);

      rig.Executed().Should().NotContain("S");
      await WaitForAsync(() => rig.Handle("q1").SnapshotPendingTimerFirings().Count == 1);
      rig.Handle("q1").SnapshotPendingTimerFirings().Should().ContainSingle()
        .Which.FireAt.Should().Be(fireAt, "the held firing keeps its original due time");
      rig.Entries().Select(e => e.SequenceId).Should().BeEquivalentTo(new[] { "A", "S" });
      await rig.StopAsync("q1");
    }

    [Fact]
    public async Task HeldNextCycleStartAndOncePerRunFiringsStayInTheirRegisters() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Once("A") }, cycle: true);
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(2);
      var handle = rig.Handle("q1");
      handle.PendingNextCycleStart.Enqueue(new SelfRescheduleEntry("n-1", "N", SelfRescheduleOption.AtQueueStart, null));
      handle.PendingOncePerRun.Enqueue(new SelfRescheduleEntry("p-1", "P", SelfRescheduleOption.OncePerRun, null));

      await rig.PassIterationsAsync(8);

      rig.Executed().Should().BeEmpty();
      await WaitForAsync(() => handle.PendingNextCycleStart.Count == 1 && handle.PendingOncePerRun.Count == 1);
      handle.PendingNextCycleStart.Should().ContainSingle(e => e.SequenceId == "N");
      handle.PendingOncePerRun.Should().ContainSingle(e => e.SequenceId == "P");
      await rig.StopAsync("q1");
    }

    [Fact]
    public async Task AHeldOncePerRunPassContinuesWithTheFirstEntryThatDidNotRun() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Once("A"), Once("B"), Once("C") });
      rig.H.Sequences.Handler = (id, ct) => {
        if (id == "A") rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
        return Task.FromResult(FakeSequenceExecution.Success(id));
      };

      await rig.Service.StartAsync("q1");
      await WaitForAsync(() => rig.Entries().Count >= 2);
      await rig.PassIterationsAsync(4);
      rig.Executed().Should().Equal("A");
      rig.Entries().Select(e => e.SequenceId).Should().Equal("B", "C");

      rig.Liveness.SetLive();
      await WaitUntilStoppedAsync(rig.Service, "q1");

      rig.Executed().Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task TwoSequencesDueAtOneTimeEachGetOneEntryAndBothRunAfterARecovery() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Timer("T1", 11), Timer("T2", 11) });
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(8);

      rig.Executed().Should().BeEmpty();
      rig.Entries().Select(e => e.SequenceId).Should().Equal("T1", "T2");
      rig.Handle("q1").Liveness.Snapshot().GatedFirings.Should().BeGreaterThanOrEqualTo(4);

      rig.Liveness.SetLive();
      await WaitUntilStoppedAsync(rig.Service, "q1");
      rig.Executed().Should().Equal("T1", "T2");
    }

    [Fact]
    public async Task AnAtQueueStartOnlyTemplateDoesNotEndWhileHeldAndRunsInOrderAfterARecovery() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { AtStart("S1"), AtStart("S2") });
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(8);

      rig.Service.IsRunning("q1").Should().BeTrue("a held at-start entry keeps the run in the loop");
      rig.Executed().Should().BeEmpty();
      rig.Entries().Select(e => e.SequenceId).Should().Equal("S1", "S2");

      rig.Liveness.SetLive();
      await WaitUntilStoppedAsync(rig.Service, "q1");
      rig.Executed().Should().Equal("S1", "S2");
    }

    [Fact]
    public async Task AHeldLiveScheduleDoesNotReplaceANewerSchedule() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Once("A") }, cycle: true);
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(2);
      var handle = rig.Handle("q1");
      handle.PendingLiveSchedules["L"] = Noon.AddMinutes(-1);
      await rig.PassIterationsAsync(4);
      handle.PendingLiveSchedules["L"].Should().Be(Noon.AddMinutes(-1), "the held schedule keeps its due time");

      var newer = Noon.AddHours(1);
      handle.PendingLiveSchedules["L"] = newer;
      await rig.PassIterationsAsync(4);

      handle.PendingLiveSchedules["L"].Should().Be(newer);
      rig.Executed().Should().NotContain("L");
      await rig.StopAsync("q1");
    }

    [Fact]
    public async Task ANonCyclingQueueDoesNotEndWhileItHoldsAFiring() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Once("A") });
      rig.Liveness.SetNotLive(DeviceLivenessReasons.InputTimeout);

      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(8);

      rig.Service.IsRunning("q1").Should().BeTrue();
      rig.Liveness.SetLive();
      await WaitUntilStoppedAsync(rig.Service, "q1");
      rig.Executed().Should().Equal("A");
    }

    [Fact]
    public async Task ALongEpisodeWritesAtMostOneEntryForEachSequencePlusOneWatchEntry() {
      var rig = new Rig();
      rig.Liveness.Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 10, QueueGracePeriodMs = 1000 };
      AddQueueWithEntries(rig.H, "q1", new[] { Once("A") }, cycle: true);
      rig.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await rig.Service.StartAsync("q1");
      await rig.PassIterationsAsync(2);
      rig.Handle("q1").AddTimerFiring(new SelfRescheduleEntry("sr-1", "S", SelfRescheduleOption.Timer, Noon.AddSeconds(-1)));
      rig.Clock.Advance(TimeSpan.FromSeconds(5));

      await rig.PassIterationsAsync(40);

      rig.Entries().Select(e => e.SequenceId).Should().BeEquivalentTo(new[] { "A", "S" });
      lock (rig.H.Log.DeviceFaults) rig.H.Log.DeviceFaults.Should().ContainSingle();

      rig.Liveness.SetLive();
      await WaitForAsync(() => rig.Executed().Contains("S") && rig.Executed().Contains("A"));
      rig.Executed().Count(s => s == "S").Should().Be(1, "the held self-reschedule firing runs one time");
      await rig.StopAsync("q1");
    }

    [Fact]
    public async Task NoChangeAfterInputDoesNotHold() {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Before("B"), Every("E"), Timer("T", 11) });
      rig.Liveness.SetNotLive(DeviceLivenessReasons.NoChangeAfterInput);

      await rig.Service.StartAsync("q1");
      await WaitUntilStoppedAsync(rig.Service, "q1");

      rig.Executed().Should().Equal("B", "T", "E");
      rig.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LiveAndUnknownRunTheGroupAsBefore(bool live) {
      var rig = new Rig();
      AddQueueWithEntries(rig.H, "q1", new[] { Before("B"), Every("E"), Timer("T", 11) });
      rig.Liveness.Report = live ? FakeSessionLivenessService.Live() : FakeSessionLivenessService.Unknown();

      await rig.Service.StartAsync("q1");
      await WaitUntilStoppedAsync(rig.Service, "q1");

      rig.Executed().Should().Equal("B", "T", "E");
      rig.Entries().Should().BeEmpty();
    }
  }
}
