using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA2000

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 171: the operator pause register of the run handle. A pause request makes the state "pausing". The
/// run loop reaches the gate between two firing groups, and the state becomes "paused" and the loop waits. A
/// resume or a cancel releases it. The register is separate from the idle pause (073) and the failure-policy
/// pause (087).
/// </summary>
public sealed class QueueRunHandleOperatorPauseTests {
  private static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

  private static QueueRunHandle NewHandle() => new() { QueueId = "q1", Cts = new CancellationTokenSource() };

  [Fact]
  public void AHandleStartsWithNoOperatorPause() {
    var handle = NewHandle();

    handle.OperatorPause.Should().Be(OperatorPauseState.None);
    handle.OperatorPauseRequested.Should().BeFalse();
    handle.OperatorPausedAt.Should().BeNull();
    handle.SnapshotPause().Paused.Should().BeFalse();
  }

  [Fact]
  public void ARequestMakesTheStatePausingButNotYetPaused() {
    var handle = NewHandle();

    handle.RequestOperatorPause(Start).Should().BeTrue();

    handle.OperatorPause.Should().Be(OperatorPauseState.Pausing);
    handle.OperatorPauseRequested.Should().BeTrue();
    handle.OperatorPauseRequestedAt.Should().Be(Start);
    handle.OperatorPausedAt.Should().BeNull();
    handle.SnapshotPause().Paused.Should().BeFalse("a pause that waits for the running group is not paused yet");
  }

  [Fact]
  public void ARepeatRequestIsSafeAndKeepsTheFirstTime() {
    var handle = NewHandle();
    handle.RequestOperatorPause(Start);

    handle.RequestOperatorPause(Start.AddSeconds(5)).Should().BeFalse();

    handle.OperatorPauseRequestedAt.Should().Be(Start);
  }

  [Fact]
  public async Task TheGateDoesNotWaitWhenNoPauseIsRequested() {
    var handle = NewHandle();

    var wait = handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);

    await wait.WaitAsync(TimeSpan.FromSeconds(1));
    handle.OperatorPause.Should().Be(OperatorPauseState.None);
  }

  [Fact]
  public async Task TheGateMakesTheStatePausedAndWaitsUntilResume() {
    var handle = NewHandle();
    handle.RequestOperatorPause(Start);

    var wait = handle.WaitAtOperatorGateAsync(Start.AddSeconds(3), CancellationToken.None);

    wait.IsCompleted.Should().BeFalse();
    handle.OperatorPause.Should().Be(OperatorPauseState.Paused);
    handle.OperatorPausedAt.Should().Be(Start.AddSeconds(3));
    var snapshot = handle.SnapshotPause();
    snapshot.Paused.Should().BeTrue();
    snapshot.Kind.Should().Be(QueuePauseKinds.Operator);
    snapshot.PausedAt.Should().Be(Start.AddSeconds(3));
    snapshot.Reason.Should().NotBeNullOrWhiteSpace();

    handle.ReleaseOperatorPause().Should().BeTrue();

    await wait.WaitAsync(TimeSpan.FromSeconds(1));
    handle.OperatorPause.Should().Be(OperatorPauseState.None);
    handle.SnapshotPause().Paused.Should().BeFalse();
  }

  [Fact]
  public async Task ACancelWhilePausingMeansTheGateDoesNotWait() {
    var handle = NewHandle();
    handle.RequestOperatorPause(Start);

    handle.ReleaseOperatorPause().Should().BeTrue();
    var wait = handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);

    await wait.WaitAsync(TimeSpan.FromSeconds(1));
    handle.OperatorPause.Should().Be(OperatorPauseState.None);
  }

  [Fact]
  public void ReleaseWithNoPauseDoesNothing() {
    NewHandle().ReleaseOperatorPause().Should().BeFalse();
  }

  [Fact]
  public async Task AStopWhilePausedEndsTheWait() {
    var handle = NewHandle();
    handle.RequestOperatorPause(Start);
    using var cts = new CancellationTokenSource();
    var wait = handle.WaitAtOperatorGateAsync(Start, cts.Token);

    await cts.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
  }

  [Fact]
  public async Task ClearOperatorPauseReleasesAWaiterAndResetsTheRegister() {
    var handle = NewHandle();
    handle.RequestOperatorPause(Start);
    var wait = handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);

    handle.ClearOperatorPause();

    await wait.WaitAsync(TimeSpan.FromSeconds(1));
    handle.OperatorPause.Should().Be(OperatorPauseState.None);
    handle.OperatorPausedAt.Should().BeNull();
    handle.OperatorPauseRequestedAt.Should().BeNull();
  }

  [Fact]
  public void ANewRequestAfterAReleaseStartsAgain() {
    var handle = NewHandle();
    handle.RequestOperatorPause(Start);
    handle.ReleaseOperatorPause();

    handle.RequestOperatorPause(Start.AddMinutes(1)).Should().BeTrue();

    handle.OperatorPauseRequestedAt.Should().Be(Start.AddMinutes(1));
  }

  [Fact]
  public void AFailurePolicyPauseIsReportedBeforeAnOperatorPause() {
    var handle = NewHandle();
    handle.EnterPolicyPause("failure policy: three failed cycles", Start);
    handle.RequestOperatorPause(Start);
    _ = handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);

    var snapshot = handle.SnapshotPause();

    snapshot.Kind.Should().Be(QueuePauseKinds.FailurePolicy);
  }

  [Fact]
  public void AnOperatorPauseIsReportedBeforeAnIdlePause() {
    var handle = NewHandle();
    handle.EnterIdlePause(Start.AddMinutes(5), Start);
    handle.RequestOperatorPause(Start);
    _ = handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);

    var snapshot = handle.SnapshotPause();

    snapshot.Kind.Should().Be(QueuePauseKinds.Operator);
  }

  [Fact]
  public void TheOperatorPauseDoesNotTouchThePolicyPauseRegister() {
    var handle = NewHandle();
    handle.RequestOperatorPause(Start);
    _ = handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);

    handle.IsPolicyPaused.Should().BeFalse();
    handle.ResumeFromPolicyPause().Should().BeFalse("only the operator pause is in force");
    handle.OperatorPause.Should().Be(OperatorPauseState.Paused);
  }

  [Fact]
  public void TheGameBackedOutFlagIsSetAndTakenOnce() {
    var handle = NewHandle();
    handle.GameBackedOut.Should().BeFalse();

    handle.MarkGameBackedOut();

    handle.GameBackedOut.Should().BeTrue();
    handle.TakeGameBackedOut().Should().BeTrue();
    handle.TakeGameBackedOut().Should().BeFalse();
    handle.GameBackedOut.Should().BeFalse();
  }
}
