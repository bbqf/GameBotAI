using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using GameBot.UnitTests.Queues.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1034 // CA1034: nested so that the tests can use the queue harness

namespace GameBot.UnitTests.Queues;

public sealed partial class QueueExecutionServiceTests {
  /// <summary>
  /// Feature 121 (FR-007 to FR-009, research R-008, R-010): one recovery attempt of one queue. The poll
  /// intervals are short and the time is real, because the waits use the timer of the time provider.
  /// </summary>
  public sealed class QueueDeviceRecoveryRunnerTests {
    private sealed class FakeProbe : IEmulatorDeviceProbe {
      public bool Responsive { get; set; } = true;

      public int Calls;

      public bool IsAvailable => true;

      public Task<bool> IsResponsiveAsync(string adbSerial, CancellationToken ct = default) {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(Responsive);
      }
    }

    private sealed class AlertRecorder : INotificationDispatcher {
      private readonly List<QueueAlert> _alerts = new();

      public List<QueueAlert> Sent { get { lock (_alerts) return _alerts.ToList(); } }

      public void Enqueue(QueueNotificationJob job) { }

      public void ResetStreaks(string queueId) { }

      public void SendAlert(QueueAlert alert) { lock (_alerts) _alerts.Add(alert); }
    }

    private sealed class Setup {
      public FakeDeviceRecoveryCoordinator Coordinator { get; } = new();
      public FakeProbe Probe { get; } = new();
      public FakeSessionManager Sessions { get; } = new();
      public FakeSessionLivenessService Liveness { get; } = new() {
        Options = new DeviceLivenessOptions { RebootReadyTimeoutMs = 300 }
      };
      public AlertRecorder Alerts { get; } = new();
      public QueueDeviceRecoveryRunner Runner { get; }
      public ExecutionQueue Queue { get; }
      public QueueRunHandle Handle { get; }
      public int Rebinds;

      public Setup(int maxAttempts = 2, string id = "q1", string serial = "emu-1") {
        Queue = new ExecutionQueue {
          Id = id, Name = "Q-" + id, EmulatorSerial = serial, EmulatorInstanceName = "LDPlayer-1",
          DeviceRecovery = new QueueDeviceRecovery { Action = "reboot-instance", AfterMs = 60000, MaxAttempts = maxAttempts, CooldownMs = 0 }
        };
        Handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };
        Handle.SessionId = Sessions.CreateSession("queue:" + id, serial).Id;
        Runner = new QueueDeviceRecoveryRunner(Coordinator, Probe, Sessions, Liveness, Alerts, TimeProvider.System,
          NullLogger.Instance, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        // The episode is open and old enough, as the watch leaves it.
        Handle.Liveness.Observe(FakeSessionLivenessService.NotLive(DeviceLivenessReasons.CaptureStalled), T0);
      }

      public Func<ExecutionQueue, QueueRunHandle, Task> Rebind => (q, h) => {
        Interlocked.Increment(ref Rebinds);
        h.SessionId = Sessions.CreateSession("queue:" + q.Id, q.EmulatorSerial).Id;
        return Task.CompletedTask;
      };

      public bool Begin() =>
        Handle.Liveness.TryBeginRecovery(DateTimeOffset.Now, TimeSpan.FromMinutes(1), Queue.DeviceRecovery!.MaxAttempts, TimeSpan.Zero);
    }

    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ASuccessfulAttempt_RebootsRebindsAndWaitsForLive() {
      var s = new Setup();
      s.Begin().Should().BeTrue();
      s.Liveness.SetLive();

      var ok = await s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);

      ok.Should().BeTrue();
      s.Coordinator.Instances.Should().ContainSingle().Which.Should().Be("LDPlayer-1");
      s.Probe.Calls.Should().BeGreaterThan(0);
      s.Rebinds.Should().Be(1);
      var snapshot = s.Handle.Liveness.Snapshot();
      snapshot.Attempts.Should().Be(1);
      snapshot.RecoveryRunning.Should().BeFalse();
      s.Alerts.Sent.Should().BeEmpty("no failure alert after a success");
    }

    [Fact]
    public async Task ARebootFailure_CountsAsAFailedAttemptAndDoesNotRebind() {
      var s = new Setup();
      s.Coordinator.Result = false;
      s.Begin().Should().BeTrue();

      var ok = await s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);

      ok.Should().BeFalse();
      s.Rebinds.Should().Be(0);
      s.Handle.Liveness.Snapshot().Attempts.Should().Be(1);
    }

    [Fact]
    public async Task AThrowingCoordinator_CountsAsAFailedAttempt() {
      var s = new Setup();
      s.Coordinator.Throws = new InvalidOperationException("boom");
      s.Begin().Should().BeTrue();

      var ok = await s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);

      ok.Should().BeFalse();
      s.Handle.Liveness.Snapshot().Attempts.Should().Be(1);
      s.Handle.Liveness.Snapshot().RecoveryRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ADeviceThatNeverAnswers_FailsAfterTheReadyTimeoutWithNoRebind() {
      var s = new Setup();
      s.Probe.Responsive = false;
      s.Begin().Should().BeTrue();

      var ok = await s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);

      ok.Should().BeFalse();
      s.Rebinds.Should().Be(0);
      s.Probe.Calls.Should().BeGreaterThan(1, "the runner polls the probe");
    }

    [Fact]
    public async Task ADeviceThatStaysNotLiveAfterTheRebind_Fails() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      s.Begin().Should().BeTrue();

      var ok = await s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);

      ok.Should().BeFalse();
      s.Rebinds.Should().Be(1);
    }

    [Fact]
    public async Task TheLastFailedAttempt_SendsOneRecoveryFailedAlertAndTheStateIsExhausted() {
      var s = new Setup(maxAttempts: 2);
      s.Coordinator.Result = false;
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      s.Begin().Should().BeTrue();
      await s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);
      s.Alerts.Sent.Should().BeEmpty("one attempt is left");
      DeviceLivenessReader.DeriveRecoveryState(s.Handle.Liveness.Snapshot(), s.Queue.DeviceRecovery).Should().Be("idle");

      s.Begin().Should().BeTrue();
      await s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);

      var alert = s.Alerts.Sent.Should().ContainSingle().Subject;
      alert.Kind.Should().Be(QueueAlertKind.RecoveryFailed);
      alert.Attempts.Should().Be(2);
      DeviceLivenessReader.DeriveRecoveryState(s.Handle.Liveness.Snapshot(), s.Queue.DeviceRecovery).Should().Be("exhausted");
      s.Begin().Should().BeFalse("the attempts are used up");
    }

    [Fact]
    public async Task WhileAnAttemptRuns_TheStateIsRunning() {
      var s = new Setup();
      var gate = new TaskCompletionSource();
      s.Coordinator.Gate = gate.Task;
      s.Begin().Should().BeTrue();

      var run = s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);
      await Task.Delay(50);

      DeviceLivenessReader.DeriveRecoveryState(s.Handle.Liveness.Snapshot(), s.Queue.DeviceRecovery).Should().Be("running");
      s.Liveness.SetLive();
      gate.SetResult();
      (await run).Should().BeTrue();
    }

    [Fact]
    public async Task AStopDuringTheReboot_ThrowsAndFreesTheRunningFlagWithNoCount() {
      var s = new Setup();
      s.Coordinator.Gate = new TaskCompletionSource().Task;
      s.Begin().Should().BeTrue();
      using var cts = new CancellationTokenSource();

      var run = s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, cts.Token);
      await Task.Delay(50);
      await cts.CancelAsync();

      await FluentActions.Awaiting(() => run).Should().ThrowAsync<OperationCanceledException>();
      var snapshot = s.Handle.Liveness.Snapshot();
      snapshot.RecoveryRunning.Should().BeFalse();
      snapshot.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task TwoQueuesOnOneInstance_EachRebindsAndCountsItsOwnAttempt() {
      var coordinator = new FakeDeviceRecoveryCoordinator();
      var a = new Setup(id: "qa", serial: "emu-a");
      var b = new Setup(id: "qb", serial: "emu-b");
      foreach (var s in new[] { a, b }) {
        s.Liveness.SetLive();
        s.Begin().Should().BeTrue();
      }
      var runnerA = new QueueDeviceRecoveryRunner(coordinator, a.Probe, a.Sessions, a.Liveness, a.Alerts, TimeProvider.System,
        NullLogger.Instance, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
      var runnerB = new QueueDeviceRecoveryRunner(coordinator, b.Probe, b.Sessions, b.Liveness, b.Alerts, TimeProvider.System,
        NullLogger.Instance, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));

      var results = await Task.WhenAll(
        runnerA.RunAttemptAsync(a.Queue, a.Handle, a.Rebind, CancellationToken.None),
        runnerB.RunAttemptAsync(b.Queue, b.Handle, b.Rebind, CancellationToken.None));

      results.Should().OnlyContain(r => r);
      a.Rebinds.Should().Be(1);
      b.Rebinds.Should().Be(1);
      a.Handle.Liveness.Snapshot().Attempts.Should().Be(1);
      b.Handle.Liveness.Snapshot().Attempts.Should().Be(1);
    }

    [Fact]
    public async Task AQueueWithNoInstanceName_FailsTheAttemptWithNoReboot() {
      var s = new Setup();
      s.Queue.EmulatorInstanceName = null;
      s.Begin().Should().BeTrue();

      var ok = await s.Runner.RunAttemptAsync(s.Queue, s.Handle, s.Rebind, CancellationToken.None);

      ok.Should().BeFalse();
      s.Coordinator.Calls.Should().Be(0);
    }
  }
}
