using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1034 // CA1034: nested so that the tests can use the queue harness

namespace GameBot.UnitTests.Queues;

public sealed partial class QueueExecutionServiceTests {
  /// <summary>
  /// Feature 106 (FR-017, FR-018, research R-012): the periodic liveness check of a queue run. After
  /// the grace period, one episode gives one <c>queue</c> log entry, one fault cycle and one call to the
  /// failure policy. The watch never stops the run by itself.
  /// </summary>
  public sealed class QueueLivenessWatchTests {
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 3, 0, 0, TimeSpan.Zero);

    private sealed class CountingNotifier : IFailureNotifier {
      private int _sent;
      public int Sent => Volatile.Read(ref _sent);

      public Task<FailureNotificationResult> NotifyAsync(FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct = default) {
        Interlocked.Increment(ref _sent);
        return Task.FromResult(new FailureNotificationResult(true, T0, null));
      }
    }

    private sealed class Setup {
      public FakeTimeProvider Clock { get; } = new(T0);
      public FakeSessionManager Sessions { get; } = new();
      public FakeSessionLivenessService Liveness { get; } = new() {
        Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 10, QueueGracePeriodMs = 120000 }
      };
      public RecordingExecutionLog Log { get; } = new();
      public CountingNotifier Notifier { get; } = new();
      public QueueRunHandle Handle { get; }
      public ExecutionQueue Queue { get; }
      public QueueLivenessWatch Watch { get; }

      public Setup(QueueFailurePolicy? policy = null, bool withEvaluator = true) {
        Queue = new ExecutionQueue { Id = "q1", Name = "Q-q1", EmulatorSerial = "emu-1", FailurePolicy = policy };
        Handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource(), RootExecutionId = "root-2" };
        Handle.SessionId = Sessions.CreateSession("queue:q1", "emu-1").Id;
        var evaluator = withEvaluator ? new QueueFailurePolicyEvaluator(Notifier, new FakeSequenceRepository(), Clock) : null;
        Watch = new QueueLivenessWatch(Queue, Handle, "root-1", Sessions, Liveness, Log, evaluator, Clock, NullLogger.Instance);
      }

      public List<(string RootExecutionId, string QueueId, string Reason)> Faults() {
        lock (Log.DeviceFaults) return Log.DeviceFaults.ToList();
      }
    }

    [Fact]
    public async Task NotLiveForLongerThanTheGraceGivesOneEntryOneFaultCycleAndOnePolicyCall() {
      var s = new Setup(new QueueFailurePolicy { ConsecutiveFailedCycles = 1, Action = QueueFailureAction.Notify });
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      (await s.Watch.CheckOnceAsync()).Should().BeFalse("the episode just opened");
      s.Clock.Advance(TimeSpan.FromMinutes(3));
      (await s.Watch.CheckOnceAsync()).Should().BeTrue();

      s.Faults().Should().ContainSingle().Which.Should().Be(("root-2", "q1", "capture_stalled"),
        "the entry uses the current root segment");
      var health = s.Handle.Cycles.SnapshotHealth();
      health.ConsecutiveFailedCycles.Should().Be(1);
      health.LastCycleSucceeded.Should().BeFalse();
      s.Handle.Cycles.SnapshotCycles(1)[0].StartedAt.Should().Be(T0, "the fault cycle starts at the start of the episode");
      await WaitForAsync(() => s.Notifier.Sent == 1);
      s.Notifier.Sent.Should().Be(1);
      s.Handle.Cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task StillNotLiveOnTheNextChecksGivesNoSecondEntry() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.InputTimeout);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(3));
      await s.Watch.CheckOnceAsync();

      for (var i = 0; i < 5; i++) {
        s.Clock.Advance(TimeSpan.FromMinutes(1));
        (await s.Watch.CheckOnceAsync()).Should().BeFalse();
      }

      s.Faults().Should().ContainSingle();
      s.Handle.Cycles.SnapshotHealth().ConsecutiveFailedCycles.Should().Be(1);
    }

    [Fact]
    public async Task LiveAndThenNotLiveAgainGivesANewEntry() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(3));
      await s.Watch.CheckOnceAsync();

      s.Liveness.SetLive();
      await s.Watch.CheckOnceAsync();
      s.Liveness.SetNotLive(DeviceLivenessReasons.TransportNotReady);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(3));
      await s.Watch.CheckOnceAsync();

      s.Faults().Select(f => f.Reason).Should().Equal("capture_stalled", "transport_not_ready");
      s.Handle.Cycles.SnapshotHealth().ConsecutiveFailedCycles.Should().Be(2,
        "a queue that does not cycle gets one failed cycle for each episode");
    }

    [Fact]
    public async Task GateEntriesDoNotStopTheWatchEntry() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Handle.Liveness.RecordGatedFiring("A").Should().BeTrue();
      s.Handle.Liveness.RecordGatedFiring("B").Should().BeTrue();
      s.Clock.Advance(TimeSpan.FromMinutes(3));

      (await s.Watch.CheckOnceAsync()).Should().BeTrue();

      s.Faults().Should().ContainSingle();
      s.Handle.Cycles.SnapshotHealth().ConsecutiveFailedCycles.Should().Be(1);
    }

    [Fact]
    public async Task NoChangeAfterInputAlsoGivesOneEntryAfterTheGrace() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.NoChangeAfterInput);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(3));

      (await s.Watch.CheckOnceAsync()).Should().BeTrue();

      s.Faults().Should().ContainSingle().Which.Reason.Should().Be("no_change_after_input");
    }

    [Fact]
    public async Task AnExceptionInTheEvaluationIsLoggedAndTheWatchContinues() {
      var s = new Setup();
      s.Liveness.Throws = new InvalidOperationException("boom");

      (await s.Watch.CheckOnceAsync()).Should().BeFalse();

      s.Liveness.Throws = null;
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(3));
      (await s.Watch.CheckOnceAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task WithNoPolicyTheRunStaysActiveAfterTheFaultCycle() {
      var s = new Setup(policy: null);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(3));

      await s.Watch.CheckOnceAsync();

      s.Handle.Cts.IsCancellationRequested.Should().BeFalse("the watch never stops the run (FR-018)");
      s.Handle.StopRequestedByPolicy.Should().BeFalse();
      s.Handle.IsPolicyPaused.Should().BeFalse();
    }

    [Fact]
    public async Task AStopPolicyActsThroughTheEvaluator() {
      var s = new Setup(new QueueFailurePolicy { ConsecutiveFailedCycles = 1, Action = QueueFailureAction.NotifyAndStop, NotifyUrl = "http://localhost:9/x" });
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(3));

      await s.Watch.CheckOnceAsync();

      s.Handle.StopRequestedByPolicy.Should().BeTrue("the evaluator marks the stop as a policy stop");
      s.Handle.Cts.IsCancellationRequested.Should().BeTrue();
      await WaitForAsync(() => s.Notifier.Sent == 1);
      s.Notifier.Sent.Should().Be(1);
    }

    [Fact]
    public async Task ACancelStopsTheWatch() {
      var s = new Setup();
      using var cts = new CancellationTokenSource();
      var run = s.Watch.RunAsync(cts.Token);
      await WaitForAsync(() => s.Liveness.Evaluations >= 3);

      await cts.CancelAsync();

      await run.WaitAsync(TimeSpan.FromSeconds(5));
      run.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task ANonCyclingQueueWithHeldFiringsGetsOneFailedCycleForTheEpisode() {
      var clock = new FakeTimeProvider(T0);
      var h = new Harness(clock);
      var liveness = new FakeSessionLivenessService {
        Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 10, QueueGracePeriodMs = 1000 }
      };
      var service = new QueueExecutionService(h.Queues, h.Runtime, h.Templates, h.Sequences, h.Sessions, h.Log,
        NullLogger<QueueExecutionService>.Instance, h.Registry, timeProvider: clock, liveness: liveness);
      AddQueueWithEntries(h, "q1", new[] { new QueueTemplateEntry { SequenceId = "A", ScheduleType = ScheduleType.OncePerRun } });
      liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      await service.StartAsync("q1");
      await WaitForAsync(() => h.Log.SequenceEntries.Count > 0);
      clock.Advance(TimeSpan.FromSeconds(5));
      await WaitForAsync(() => h.Registry.TryGet("q1", out var hd) && hd.Cycles.SnapshotHealth().ConsecutiveFailedCycles > 0);

      h.Registry.TryGet("q1", out var handle).Should().BeTrue();
      handle.Cycles.SnapshotHealth().ConsecutiveFailedCycles.Should().Be(1);
      lock (h.Log.DeviceFaults) h.Log.DeviceFaults.Should().ContainSingle();
      service.IsRunning("q1").Should().BeTrue();

      await service.StopAsync("q1");
      await WaitUntilStoppedAsync(service, "q1");
    }
  }
}
