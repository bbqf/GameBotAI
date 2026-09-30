using System;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Service.Contracts.Queues;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1034 // CA1034: nested so that the tests can use the queue harness

namespace GameBot.UnitTests.Queues;

public sealed partial class QueueExecutionServiceTests {
  /// <summary>
  /// Feature 121 (FR-017, research R-022): <c>recoveryState</c>, <c>alertSent</c> and
  /// <c>recoveryAttempts</c> of <c>health.deviceLiveness</c>.
  /// </summary>
  public sealed class DeviceLivenessReaderRecoveryStateTests {
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 3, 0, 0, TimeSpan.Zero);
    private static readonly QueueDeviceRecovery Reboot = new() { Action = "reboot-instance", MaxAttempts = 2, AfterMs = 60000, CooldownMs = 0 };

    private sealed class Setup {
      public FakeTimeProvider Clock { get; } = new(T0);
      public FakeSessionManager Sessions { get; } = new();
      public FakeSessionLivenessService Liveness { get; } = new();
      public QueueRunHandle Handle { get; }
      public DeviceLivenessReader Reader { get; }

      public Setup() {
        Handle = new QueueRunHandle { QueueId = "q1", Cts = new System.Threading.CancellationTokenSource() };
        Handle.SessionId = Sessions.CreateSession("queue:q1", "emu-1").Id;
        Reader = new DeviceLivenessReader(Sessions, Liveness, Clock);
      }
    }

    [Fact]
    public void WithNoOpenEpisodeTheStateIsIdleAndTheCountersAreZero() {
      var s = new Setup();
      s.Liveness.SetLive();

      var r = s.Reader.Project(s.Handle, Reboot)!;

      r.RecoveryState.Should().Be("idle");
      r.AlertSent.Should().BeFalse();
      r.RecoveryAttempts.Should().Be(0);
    }

    [Fact]
    public void WithNoRecoveryRunningAndAttemptsLeftTheStateIsIdle() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      s.Reader.Project(s.Handle, Reboot);
      s.Handle.Liveness.TryBeginRecovery(T0.AddMinutes(5), TimeSpan.FromMinutes(1), 2, TimeSpan.Zero).Should().BeTrue();
      s.Handle.Liveness.EndRecoveryAttempt(T0.AddMinutes(6));

      var r = s.Reader.Project(s.Handle, Reboot)!;

      r.RecoveryState.Should().Be("idle");
      r.RecoveryAttempts.Should().Be(1);
    }

    [Fact]
    public void WhileAnAttemptRunsTheStateIsRunning() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      s.Reader.Project(s.Handle, Reboot);
      s.Handle.Liveness.TryBeginRecovery(T0.AddMinutes(5), TimeSpan.FromMinutes(1), 2, TimeSpan.Zero).Should().BeTrue();

      var r = s.Reader.Project(s.Handle, Reboot)!;

      r.RecoveryState.Should().Be("running");
    }

    [Fact]
    public void WhenTheAttemptsAreUsedUpAndTheDeviceIsNotLiveTheStateIsExhausted() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      s.Reader.Project(s.Handle, Reboot);
      for (var i = 0; i < 2; i++) {
        s.Handle.Liveness.TryBeginRecovery(T0.AddMinutes(5 + i), TimeSpan.FromMinutes(1), 2, TimeSpan.Zero).Should().BeTrue();
        s.Handle.Liveness.EndRecoveryAttempt(T0.AddMinutes(5 + i));
      }

      var r = s.Reader.Project(s.Handle, Reboot)!;

      r.RecoveryState.Should().Be("exhausted");
      r.RecoveryAttempts.Should().Be(2);
    }

    [Fact]
    public void ALiveDeviceAfterUsedUpAttemptsIsIdleAgain() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      s.Reader.Project(s.Handle, Reboot);
      for (var i = 0; i < 2; i++) {
        s.Handle.Liveness.TryBeginRecovery(T0.AddMinutes(5 + i), TimeSpan.FromMinutes(1), 2, TimeSpan.Zero);
        s.Handle.Liveness.EndRecoveryAttempt(T0.AddMinutes(5 + i));
      }
      s.Liveness.SetLive();

      var r = s.Reader.Project(s.Handle, Reboot)!;

      r.RecoveryState.Should().Be("idle");
      r.RecoveryAttempts.Should().Be(0);
    }

    [Fact]
    public void AQueueWithNoRecoveryNeverShowsExhausted() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);

      var r = s.Reader.Project(s.Handle, null)!;

      r.RecoveryState.Should().Be("idle");
    }

    [Fact]
    public void AlertSentShowsTheClaimOfTheOpenEpisode() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      s.Reader.Project(s.Handle, Reboot);
      s.Handle.Liveness.TryClaimAlert(T0.AddMinutes(10), TimeSpan.FromMinutes(5)).Should().BeTrue();

      var r = s.Reader.Project(s.Handle, Reboot)!;

      r.AlertSent.Should().BeTrue();
    }

    [Fact]
    public void TheStateIsAlwaysOneOfTheThreeValues() {
      var s = new Setup();
      s.Liveness.SetNotLive(DeviceLivenessReasons.InputTimeout);

      var r = s.Reader.Project(s.Handle, Reboot)!;

      r.RecoveryState.Should().BeOneOf(DeviceRecoveryStates.Idle, DeviceRecoveryStates.Running, DeviceRecoveryStates.Exhausted);
    }
  }
}
