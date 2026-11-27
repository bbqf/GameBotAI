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

    internal sealed class AlertRecorder : INotificationDispatcher {
      private readonly List<QueueAlert> _alerts = new();

      public List<QueueAlert> Sent { get { lock (_alerts) return _alerts.ToList(); } }

      public void Enqueue(QueueNotificationJob job) { }

      public void ResetStreaks(string queueId) { }

      public void SendAlert(QueueAlert alert) { lock (_alerts) _alerts.Add(alert); }

      public System.Threading.Tasks.Task<TextSendOutcome> SendText(string text, string scopeKey, string sequenceId, System.Threading.CancellationToken ct)
        => System.Threading.Tasks.Task.FromResult(TextSendOutcome.NoTarget());
    }

    private sealed class Setup {
      public FakeTimeProvider Clock { get; }
      public FakeSessionManager Sessions { get; } = new();
      public FakeSessionLivenessService Liveness { get; } = new() {
        Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 10, QueueGracePeriodMs = 120000 }
      };
      public RecordingExecutionLog Log { get; } = new();
      public CountingNotifier Notifier { get; } = new();
      public QueueRunHandle Handle { get; }
      public ExecutionQueue Queue { get; }
      public QueueLivenessWatch Watch { get; }

      public AlertRecorder Alerts { get; } = new();

      public Setup(
        QueueFailurePolicy? policy = null,
        bool withEvaluator = true,
        bool withAlerts = false,
        int alertAfterMs = 300000,
        QueueDeviceRecovery? recovery = null,
        QueueDeviceRecoveryRunner? runner = null,
        Func<ExecutionQueue, QueueRunHandle, Task>? rebind = null,
        Func<CancellationToken, Task<ExecutionQueue?>>? readQueue = null,
        FakeTimeProvider? clock = null,
        Func<ExecutionQueue, QueueRunHandle, string?>? tryBind = null) {
        Clock = clock ?? new FakeTimeProvider(T0);
        Liveness.Options.AlertAfterMs = alertAfterMs;
        Queue = new ExecutionQueue {
          Id = "q1", Name = "Q-q1", EmulatorSerial = "emu-1", FailurePolicy = policy,
          DeviceRecovery = recovery, EmulatorInstanceName = recovery is null ? null : "LDPlayer-1"
        };
        Handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource(), RootExecutionId = "root-2" };
        Handle.SessionId = Sessions.CreateSession("queue:q1", "emu-1").Id;
        var evaluator = withEvaluator ? new QueueFailurePolicyEvaluator(Notifier, new FakeSequenceRepository(), Clock) : null;
        Watch = new QueueLivenessWatch(Queue, Handle, "root-1", Sessions, Liveness, Log, evaluator, Clock, NullLogger.Instance,
          withAlerts ? Alerts : null, runner, rebind, readQueue, tryBind);
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
    public async Task TheWatchSeesCaptureStalledWhileAStepRuns() {
      // Feature 197 (FR-012): the watch runs every QueueCheckIntervalMs for the whole run. It does not wait
      // for the next sequence start, so it sees a stall while a long step of the run still runs.
      var s = new Setup();
      var step = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      using var stop = new CancellationTokenSource();
      var watch = s.Watch.RunAsync(stop.Token);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await WaitForAsync(() => s.Handle.Liveness.Snapshot().NotLiveSince is not null);
      s.Clock.Advance(TimeSpan.FromMinutes(3));

      await WaitForAsync(() => s.Faults().Count == 1);

      step.Task.IsCompleted.Should().BeFalse("the step still runs");
      s.Faults().Should().ContainSingle().Which.Reason.Should().Be("capture_stalled");
      step.SetResult();
      await stop.CancelAsync();
      await watch;
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

    // ---- Feature 121: alerts ----

    [Fact]
    public async Task Alert_NotLiveForLongerThanTheAlertTimeGivesOneAlertOnly() {
      var s = new Setup(withAlerts: true, alertAfterMs: 120000);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Alerts.Sent.Should().BeEmpty("the episode just opened");

      s.Clock.Advance(TimeSpan.FromMinutes(3));
      await s.Watch.CheckOnceAsync();
      for (var i = 0; i < 5; i++) {
        s.Clock.Advance(TimeSpan.FromMinutes(1));
        await s.Watch.CheckOnceAsync();
      }

      var alert = s.Alerts.Sent.Should().ContainSingle().Subject;
      alert.Kind.Should().Be(QueueAlertKind.NotLive);
      alert.QueueId.Should().Be("q1");
      alert.Reason.Should().Be("capture_stalled");
    }

    [Fact]
    public async Task Alert_TheCallbackSetsTheLastNotificationOfTheQueue() {
      var s = new Setup(withAlerts: true, alertAfterMs: 1000);
      s.Liveness.SetNotLive(DeviceLivenessReasons.InputTimeout);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromSeconds(5));
      await s.Watch.CheckOnceAsync();

      s.Alerts.Sent.Single().OnCompleted!.Invoke(T0, false, "chat not found");

      var last = s.Handle.LastNotification;
      last.At.Should().Be(T0);
      last.Succeeded.Should().BeFalse();
      last.Error.Should().Be("chat not found");
    }

    [Fact]
    public async Task Alert_LiveAgainAfterAnAlertGivesOneLiveAgainMessage() {
      var s = new Setup(withAlerts: true, alertAfterMs: 1000);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromSeconds(5));
      await s.Watch.CheckOnceAsync();

      s.Liveness.SetLive();
      await s.Watch.CheckOnceAsync();
      await s.Watch.CheckOnceAsync();

      s.Alerts.Sent.Select(a => a.Kind).Should().Equal(QueueAlertKind.NotLive, QueueAlertKind.LiveAgain);
    }

    [Fact]
    public async Task Alert_LiveBeforeTheAlertTimeGivesNoMessage() {
      var s = new Setup(withAlerts: true, alertAfterMs: 300000);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(1));
      await s.Watch.CheckOnceAsync();

      s.Liveness.SetLive();
      await s.Watch.CheckOnceAsync();

      s.Alerts.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Alert_AStopWithNoAlertGivesNoLiveAgainMessage() {
      var s = new Setup(withAlerts: true, alertAfterMs: 300000);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();

      // A stop ends the watch. A later observation of a new run has a new episode object.
      s.Liveness.SetLive();
      await s.Watch.CheckOnceAsync();

      s.Alerts.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Alert_ANewEpisodeGivesANewAlert() {
      var s = new Setup(withAlerts: true, alertAfterMs: 1000);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromSeconds(5));
      await s.Watch.CheckOnceAsync();
      s.Liveness.SetLive();
      await s.Watch.CheckOnceAsync();

      s.Liveness.SetNotLive(DeviceLivenessReasons.InputTimeout);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromSeconds(5));
      await s.Watch.CheckOnceAsync();

      s.Alerts.Sent.Select(a => a.Kind).Should().Equal(QueueAlertKind.NotLive, QueueAlertKind.LiveAgain, QueueAlertKind.NotLive);
      s.Alerts.Sent[2].Reason.Should().Be("input_timeout");
    }

    [Fact]
    public async Task Alert_AFreshWatchAfterARestartGivesExactlyOneAlert() {
      var first = new Setup(withAlerts: true, alertAfterMs: 1000);
      first.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await first.Watch.CheckOnceAsync();
      first.Clock.Advance(TimeSpan.FromSeconds(5));
      await first.Watch.CheckOnceAsync();

      // A service restart makes a new run handle, so a new episode and a new timer.
      var second = new Setup(withAlerts: true, alertAfterMs: 1000);
      second.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await second.Watch.CheckOnceAsync();
      second.Alerts.Sent.Should().BeEmpty("the timer starts again");
      second.Clock.Advance(TimeSpan.FromSeconds(5));
      await second.Watch.CheckOnceAsync();
      await second.Watch.CheckOnceAsync();

      second.Alerts.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Alert_AnOpenLiveAgainClaimIsSentWhenTheWatchEnds() {
      var s = new Setup(withAlerts: true, alertAfterMs: 1000);
      s.Handle.Liveness.Observe(FakeSessionLivenessService.NotLive(DeviceLivenessReasons.CaptureStalled), T0);
      s.Handle.Liveness.TryClaimAlert(T0.AddMinutes(1), TimeSpan.FromSeconds(1)).Should().BeTrue();
      // The gate of the run loop sees the device live, and the run ends before the next watch check.
      s.Handle.Liveness.Observe(FakeSessionLivenessService.Live(), T0.AddMinutes(2));
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await s.Watch.RunAsync(cts.Token);

      s.Alerts.Sent.Select(a => a.Kind).Should().Equal(QueueAlertKind.LiveAgain);
    }

    [Fact]
    public async Task Alert_AStopWhileTheDeviceIsStillNotLiveSendsNoLiveAgain() {
      var s = new Setup(withAlerts: true, alertAfterMs: 1000);
      s.Handle.Liveness.Observe(FakeSessionLivenessService.NotLive(DeviceLivenessReasons.CaptureStalled), T0);
      s.Handle.Liveness.TryClaimAlert(T0.AddMinutes(1), TimeSpan.FromSeconds(1)).Should().BeTrue();
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await s.Watch.RunAsync(cts.Token);

      s.Alerts.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Alert_WithNoAlertSenderTheWatchSendsNothingAndStillRecordsTheFaultCycle() {
      var s = new Setup(withAlerts: false, alertAfterMs: 1000);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(3));

      (await s.Watch.CheckOnceAsync()).Should().BeTrue();
    }

    // ---- Feature 121: recovery start ----

    private sealed class NoProbe : GameBot.Service.Services.EnsureEmulatorRunning.IEmulatorDeviceProbe {
      public bool IsAvailable => true;

      public Task<bool> IsResponsiveAsync(string adbSerial, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public async Task Recovery_StartsOneAttemptAfterAfterMsWhenTheActionIsReboot() {
      var coordinator = new Fakes.FakeDeviceRecoveryCoordinator { Result = false };
      var sessions = new FakeSessionManager();
      var liveness = new FakeSessionLivenessService { Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 10, RebootReadyTimeoutMs = 1000 } };
      var runner = new QueueDeviceRecoveryRunner(coordinator, new NoProbe(), sessions, liveness);
      var recovery = new QueueDeviceRecovery { Action = "reboot-instance", AfterMs = 60000, MaxAttempts = 1, CooldownMs = 0 };
      var s = new Setup(withAlerts: true, recovery: recovery, runner: runner, rebind: (_, _) => Task.CompletedTask);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromSeconds(30));
      await s.Watch.CheckOnceAsync();
      coordinator.Calls.Should().Be(0, "the episode is younger than afterMs");

      s.Clock.Advance(TimeSpan.FromSeconds(60));
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;

      coordinator.Calls.Should().Be(1, "maxAttempts is 1");
      s.Handle.Liveness.Snapshot().Attempts.Should().Be(1);
      s.Handle.Liveness.Snapshot().RecoveryRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Recovery_DoesNotStartWhenTheActionIsNoneOrTheFieldIsNull() {
      var coordinator = new Fakes.FakeDeviceRecoveryCoordinator();
      var runner = new QueueDeviceRecoveryRunner(coordinator, new NoProbe(), new FakeSessionManager(), new FakeSessionLivenessService());
      foreach (var recovery in new QueueDeviceRecovery?[] { null, new QueueDeviceRecovery { Action = "none", AfterMs = 60000 } }) {
        var s = new Setup(withAlerts: true, recovery: recovery, runner: runner, rebind: (_, _) => Task.CompletedTask);
        s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
        await s.Watch.CheckOnceAsync();
        s.Clock.Advance(TimeSpan.FromHours(1));
        await s.Watch.CheckOnceAsync();
        await s.Watch.RecoveryTask;
      }

      coordinator.Calls.Should().Be(0);
    }

    // ---- Feature 199 (FR-012a): a saved change of deviceRecovery applies to the run ----

    // The stored queue that the test changes. The read delegate gives a new object for each read, as the repository does.
    private sealed class StoredQueue {
      public QueueDeviceRecovery? Recovery { get; set; }
      public string? InstanceName { get; set; } = "LDPlayer-1";
      public Exception? Throws { get; set; }
      public bool GiveNull { get; set; }

      public Task<ExecutionQueue?> ReadAsync(CancellationToken ct) {
        if (Throws is { } ex) throw ex;
        if (GiveNull) return Task.FromResult<ExecutionQueue?>(null);
        return Task.FromResult<ExecutionQueue?>(new ExecutionQueue {
          Id = "q1", Name = "Q-q1", EmulatorSerial = "emu-1", EmulatorInstanceName = InstanceName, DeviceRecovery = Recovery
        });
      }
    }

    private static QueueDeviceRecovery Reboot(int afterMs = 60000, int maxAttempts = 2, int cooldownMs = 0) =>
      new() { Action = "reboot-instance", AfterMs = afterMs, MaxAttempts = maxAttempts, CooldownMs = cooldownMs };

    private static (Setup Setup, Fakes.FakeDeviceRecoveryCoordinator Coordinator, StoredQueue Stored) NewLiveApplySetup(
      QueueDeviceRecovery? startRecovery, Task? gate = null) {
      var coordinator = new Fakes.FakeDeviceRecoveryCoordinator { Result = false, Gate = gate };
      var liveness = new FakeSessionLivenessService { Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 10, RebootReadyTimeoutMs = 1000 } };
      // The runner and the watch share one clock, so the end time of an attempt is on the same time line.
      var clock = new FakeTimeProvider(T0);
      var runner = new QueueDeviceRecoveryRunner(coordinator, new NoProbe(), new FakeSessionManager(), liveness, null, clock);
      var stored = new StoredQueue { Recovery = startRecovery };
      var s = new Setup(withAlerts: true, recovery: startRecovery, runner: runner, rebind: (_, _) => Task.CompletedTask, readQueue: stored.ReadAsync, clock: clock);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      return (s, coordinator, stored);
    }

    [Fact]
    public async Task LiveApply_TurnOnDuringAnOldEpisodeStartsOneAttemptAtTheNextCheck() {
      var (s, coordinator, stored) = NewLiveApplySetup(null);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(2));
      await s.Watch.CheckOnceAsync();
      coordinator.Calls.Should().Be(0, "the run started with no recovery");

      stored.Recovery = Reboot(afterMs: 60000, maxAttempts: 1);
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;

      coordinator.Calls.Should().Be(1);
    }

    [Fact]
    public async Task LiveApply_TurnOffStopsNewAttempts() {
      var (s, coordinator, stored) = NewLiveApplySetup(Reboot(maxAttempts: 3));
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(2));
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;
      coordinator.Calls.Should().Be(1);

      stored.Recovery = new QueueDeviceRecovery { Action = "none", AfterMs = 60000 };
      for (var i = 0; i < 3; i++) {
        s.Clock.Advance(TimeSpan.FromMinutes(1));
        await s.Watch.CheckOnceAsync();
        await s.Watch.RecoveryTask;
      }

      coordinator.Calls.Should().Be(1);
    }

    [Fact]
    public async Task LiveApply_LoweringMaxAttemptsBelowTheCountStopsNewAttempts() {
      var (s, coordinator, stored) = NewLiveApplySetup(Reboot(maxAttempts: 3));
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(2));
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;
      coordinator.Calls.Should().Be(1);

      stored.Recovery = Reboot(maxAttempts: 1);
      for (var i = 0; i < 3; i++) {
        s.Clock.Advance(TimeSpan.FromMinutes(1));
        await s.Watch.CheckOnceAsync();
        await s.Watch.RecoveryTask;
      }

      coordinator.Calls.Should().Be(1);
    }

    [Fact]
    public async Task LiveApply_AReadThatFailsOrGivesNullKeepsTheLastKnownSettings() {
      var (s, coordinator, stored) = NewLiveApplySetup(Reboot(maxAttempts: 2));
      await s.Watch.CheckOnceAsync();
      stored.Throws = new InvalidOperationException("the file is locked");
      s.Clock.Advance(TimeSpan.FromMinutes(2));

      var throwing = async () => await s.Watch.CheckOnceAsync();
      await throwing.Should().NotThrowAsync();
      await s.Watch.RecoveryTask;
      coordinator.Calls.Should().Be(1, "the watch used the last known settings");

      stored.Throws = null;
      stored.GiveNull = true;
      s.Clock.Advance(TimeSpan.FromMinutes(1));
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;
      coordinator.Calls.Should().Be(2, "a null read keeps the last known settings, so the second attempt starts");
    }

    [Fact]
    public async Task LiveApply_AChangeKeepsTheAttemptCountOfTheEpisode() {
      var (s, _, stored) = NewLiveApplySetup(Reboot(maxAttempts: 3));
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(2));
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;
      s.Handle.Liveness.Snapshot().Attempts.Should().Be(1);

      stored.Recovery = Reboot(afterMs: 120000, maxAttempts: 5);
      s.Clock.Advance(TimeSpan.FromSeconds(10));
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;

      s.Handle.Liveness.Snapshot().Attempts.Should().Be(2, "the count continues; it does not start again at 0");
    }

    [Fact]
    public async Task LiveApply_TurnOffDuringAnAttemptLetsTheAttemptEndAndStartsNoNewOne() {
      var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var (s, coordinator, stored) = NewLiveApplySetup(Reboot(maxAttempts: 3), gate.Task);
      await s.Watch.CheckOnceAsync();
      s.Clock.Advance(TimeSpan.FromMinutes(2));
      await s.Watch.CheckOnceAsync();
      await WaitForAsync(() => coordinator.Calls == 1);
      s.Handle.Liveness.Snapshot().RecoveryRunning.Should().BeTrue();

      stored.Recovery = new QueueDeviceRecovery { Action = "none", AfterMs = 60000 };
      await s.Watch.CheckOnceAsync();
      s.Handle.Liveness.Snapshot().RecoveryRunning.Should().BeTrue("the attempt that runs is not stopped");

      gate.SetResult();
      await s.Watch.RecoveryTask;
      s.Handle.Liveness.Snapshot().RecoveryRunning.Should().BeFalse();
      for (var i = 0; i < 3; i++) {
        s.Clock.Advance(TimeSpan.FromMinutes(1));
        await s.Watch.CheckOnceAsync();
        await s.Watch.RecoveryTask;
      }

      coordinator.Calls.Should().Be(1);
    }

    // ---- Feature 201: the watch with no bound session ----

    private static (Setup Setup, Fakes.FakeDeviceRecoveryCoordinator Coordinator, AlertRecorder RunnerAlerts) NewNoSessionSetup(
      int maxAttempts = 2, Func<ExecutionQueue, QueueRunHandle, string?>? tryBind = null) {
      var coordinator = new Fakes.FakeDeviceRecoveryCoordinator { Result = false };
      var liveness = new FakeSessionLivenessService { Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 10, RebootReadyTimeoutMs = 1000 } };
      var clock = new FakeTimeProvider(T0);
      var runnerAlerts = new AlertRecorder();
      var runner = new QueueDeviceRecoveryRunner(coordinator, new NoProbe(), new FakeSessionManager(), liveness, runnerAlerts, clock);
      var s = new Setup(withAlerts: true, alertAfterMs: 1000, recovery: Reboot(afterMs: 60000, maxAttempts: maxAttempts), runner: runner,
        rebind: (_, _) => Task.CompletedTask, clock: clock, tryBind: tryBind ?? ((_, _) => null));
      s.Handle.SessionId = null;
      s.Handle.DeviceRecoveryReboots = true;
      return (s, coordinator, runnerAlerts);
    }

    [Fact]
    public async Task NoSession_TheWatchObservesTransportNotReadyAlertsAndStartsARecoveryAttempt() {
      var (s, coordinator, _) = NewNoSessionSetup();

      (await s.Watch.CheckOnceAsync()).Should().BeFalse();
      var snapshot = s.Handle.Liveness.Snapshot();
      snapshot.State.Should().Be(DeviceLivenessStates.NotLive);
      snapshot.Reason.Should().Be(DeviceLivenessReasons.TransportNotReady);
      s.Liveness.Evaluations.Should().Be(0, "there is no session to evaluate");

      s.Clock.Advance(TimeSpan.FromSeconds(2));
      await s.Watch.CheckOnceAsync();
      s.Alerts.Sent.Should().ContainSingle(a => a.Kind == QueueAlertKind.NotLive)
        .Which.Reason.Should().Be(DeviceLivenessReasons.TransportNotReady);
      coordinator.Calls.Should().Be(0, "the episode is younger than afterMs");

      s.Clock.Advance(TimeSpan.FromMinutes(1));
      await s.Watch.CheckOnceAsync();
      await s.Watch.RecoveryTask;
      coordinator.Calls.Should().Be(1);
      s.Handle.Liveness.Snapshot().Attempts.Should().Be(1);
    }

    [Fact]
    public async Task NoSession_AllAttemptsFailedGivesExactlyOneRecoveryFailedAlert() {
      var (s, coordinator, runnerAlerts) = NewNoSessionSetup(maxAttempts: 2);
      await s.Watch.CheckOnceAsync();
      for (var i = 0; i < 5; i++) {
        s.Clock.Advance(TimeSpan.FromMinutes(1.5));
        await s.Watch.CheckOnceAsync();
        await s.Watch.RecoveryTask;
      }

      coordinator.Calls.Should().Be(2);
      runnerAlerts.Sent.Count(a => a.Kind == QueueAlertKind.RecoveryFailed).Should().Be(1);
      s.Handle.Cts.IsCancellationRequested.Should().BeFalse("the watch never stops the run");
    }

    [Fact]
    public async Task NoSession_ABindThatGivesASessionMakesTheWatchEvaluateIt() {
      Setup? holder = null;
      string? bound = null;
      var (s, _, _) = NewNoSessionSetup(tryBind: (_, h) => {
        bound ??= holder!.Sessions.CreateSession("queue:q1", "emu-1").Id;
        h.SessionId = bound;
        return bound;
      });
      holder = s;
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      await s.Watch.CheckOnceAsync();

      bound.Should().NotBeNull();
      s.Liveness.Evaluations.Should().Be(1);
      s.Handle.Liveness.Snapshot().Reason.Should().Be(DeviceLivenessReasons.CaptureStalled);
    }

    [Fact]
    public async Task NoSession_WithRecoveryOffTheCheckDoesNothing() {
      var (s, coordinator, _) = NewNoSessionSetup();
      s.Handle.DeviceRecoveryReboots = false;

      (await s.Watch.CheckOnceAsync()).Should().BeFalse();

      s.Handle.Liveness.Snapshot().LastReport.Should().BeNull();
      coordinator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task TheStoredRecoverySettingSetsTheFlagOfTheHandleAtEachCheck() {
      var (s, _, stored) = NewLiveApplySetup(Reboot());
      s.Handle.DeviceRecoveryReboots = true;
      stored.Recovery = new QueueDeviceRecovery { Action = "none", AfterMs = 60000 };

      await s.Watch.CheckOnceAsync();
      s.Handle.DeviceRecoveryReboots.Should().BeFalse();

      stored.Recovery = Reboot();
      await s.Watch.CheckOnceAsync();
      s.Handle.DeviceRecoveryReboots.Should().BeTrue();
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
