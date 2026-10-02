using System;
using System.Threading;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.StepThrough;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

public sealed class StepThroughSessionGuardTests {
  private const string Serial = "emulator-5558";

  private sealed class Rig {
    public QueueRunRegistry Runs { get; } = new();

    public DeviceClaimRegistry Claims { get; } = new();

    public StepThroughSessionGuard Guard { get; }

    public Rig() {
      Guard = new StepThroughSessionGuard(Runs, Claims, TimeProvider.System);
    }

    public QueueRunHandle AddQueue(string id = "q1", string name = "Daily", string serial = Serial) {
      var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };
      Runs.TryAdd(id, handle).Should().BeTrue();
      Claims.TryClaim(serial, id, name).Should().BeTrue();
      return handle;
    }
  }

  [Fact]
  public void NoQueueOnTheDeviceGivesNoQueue() {
    var rig = new Rig();

    rig.Guard.Inspect(Serial).State.Should().Be(SessionQueueState.NoQueue);
    rig.Guard.Inspect(null).State.Should().Be(SessionQueueState.NoQueue);
  }

  [Fact]
  public void ClaimWithoutARunHandleGivesNoQueue() {
    var rig = new Rig();
    rig.Claims.TryClaim(Serial, "q1", "Daily");

    rig.Guard.Inspect(Serial).State.Should().Be(SessionQueueState.NoQueue);
  }

  [Fact]
  public void RunningQueueWithoutAFiringIsRunningAndNamed() {
    var rig = new Rig();
    rig.AddQueue();

    var status = rig.Guard.Inspect(Serial);

    status.State.Should().Be(SessionQueueState.Running);
    status.QueueId.Should().Be("q1");
    status.QueueName.Should().Be("Daily");
  }

  [Fact]
  public void QueueWithAFiringNowIsFiringActive() {
    var rig = new Rig();
    var handle = rig.AddQueue();
    handle.SetCurrentSequence("seq", DateTimeOffset.UtcNow);

    rig.Guard.Inspect(Serial).State.Should().Be(SessionQueueState.FiringActive);
  }

  [Fact]
  public void PauseTakesThePausedStateWithTheStepThroughReason() {
    var rig = new Rig();
    var handle = rig.AddQueue();

    var outcome = rig.Guard.TryPause(Serial, out var status);

    outcome.Should().Be(QueuePauseOutcome.Paused);
    handle.IsPolicyPaused.Should().BeTrue();
    handle.PauseReason.Should().Be(StepThroughSessionGuard.PauseReason);
    status.State.Should().Be(SessionQueueState.Paused);
    status.PausedByStepThrough.Should().BeTrue();
  }

  [Fact]
  public void PauseIsRefusedWhileAFiringRunsAndTheQueueStaysUnpaused() {
    var rig = new Rig();
    var handle = rig.AddQueue();
    handle.SetCurrentSequence("seq", DateTimeOffset.UtcNow);

    var outcome = rig.Guard.TryPause(Serial, out _);

    outcome.Should().Be(QueuePauseOutcome.FiringActive);
    handle.IsPolicyPaused.Should().BeFalse();
  }

  [Fact]
  public void PauseOfAnAlreadyPausedQueueDoesNotChangeItsPause() {
    var rig = new Rig();
    var handle = rig.AddQueue();
    handle.EnterPolicyPause("failure policy", DateTimeOffset.UtcNow);

    var outcome = rig.Guard.TryPause(Serial, out var status);

    outcome.Should().Be(QueuePauseOutcome.AlreadyPaused);
    handle.PauseReason.Should().Be("failure policy");
    status.PausedByStepThrough.Should().BeFalse();
  }

  [Fact]
  public void PauseWithNoQueueGivesNoQueue() {
    var rig = new Rig();

    rig.Guard.TryPause(Serial, out _).Should().Be(QueuePauseOutcome.NoQueue);
  }

  [Fact]
  public void ResumeReleasesOnlyAPauseThatTheStepThroughMade() {
    var rig = new Rig();
    var handle = rig.AddQueue();
    rig.Guard.TryPause(Serial, out _);

    rig.Guard.Resume("q1").Should().BeTrue();

    handle.IsPolicyPaused.Should().BeFalse();
  }

  [Fact]
  public void ResumeLeavesAPauseFromAnotherCause() {
    var rig = new Rig();
    var handle = rig.AddQueue();
    handle.EnterPolicyPause("failure policy", DateTimeOffset.UtcNow);

    rig.Guard.Resume("q1").Should().BeFalse();

    handle.IsPolicyPaused.Should().BeTrue();
  }

  [Fact]
  public void ResumeOfAnUnknownQueueDoesNothing() {
    var rig = new Rig();

    rig.Guard.Resume("nope").Should().BeFalse();
  }

  [Fact]
  public void SecondPauseByTheStepThroughStillReportsPausedState() {
    var rig = new Rig();
    rig.AddQueue();
    rig.Guard.TryPause(Serial, out _);

    var outcome = rig.Guard.TryPause(Serial, out var status);

    outcome.Should().Be(QueuePauseOutcome.AlreadyPaused);
    status.PausedByStepThrough.Should().BeTrue();
  }
}
