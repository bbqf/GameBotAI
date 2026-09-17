using System;
using System.Threading;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA2000

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 096 (issue #199): the run handle's pause registers and the combined pause snapshot that
/// <c>health.paused</c> is projected from. An idle pause and a failure-policy pause both read as paused,
/// told apart by kind, with the failure-policy pause winning when both are in force.
/// </summary>
public sealed class QueueRunHandlePauseTests {
  private static readonly DateTimeOffset Start = new(2026, 9, 16, 20, 57, 36, TimeSpan.FromHours(2));

  private static QueueRunHandle NewHandle() => new() { QueueId = "q1", Cts = new CancellationTokenSource() };

  // ── Idle-pause register (FR-002) ─────────────────────────────────────────────────────────────

  [Fact]
  public void EnterIdlePauseRecordsStartAndResumeInstants() {
    var handle = NewHandle();

    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(2), Start);

    handle.IdlePausedAt.Should().Be(Start);
    handle.IdlePausedUntil.Should().Be(Start + TimeSpan.FromMinutes(2));
  }

  [Fact]
  public void ReenteringAnOngoingIdlePauseKeepsItsStartInstant() {
    var handle = NewHandle();
    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(10), Start);

    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(3), Start + TimeSpan.FromSeconds(30));

    handle.IdlePausedAt.Should().Be(Start, "a resume-time update is the same continuous pause");
    handle.IdlePausedUntil.Should().Be(Start + TimeSpan.FromMinutes(3));
  }

  [Fact]
  public void ClearIdlePauseResetsBothInstants() {
    var handle = NewHandle();
    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(2), Start);

    handle.ClearIdlePause();

    handle.IdlePausedAt.Should().BeNull();
    handle.IdlePausedUntil.Should().BeNull();
  }

  [Fact]
  public void ANewIdlePauseAfterAClearRecordsItsOwnStart() {
    var handle = NewHandle();
    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(2), Start);
    handle.ClearIdlePause();

    var later = Start + TimeSpan.FromMinutes(5);
    handle.EnterIdlePause(later + TimeSpan.FromMinutes(2), later);

    handle.IdlePausedAt.Should().Be(later);
  }

  // ── Combined snapshot (US1) ──────────────────────────────────────────────────────────────────

  [Fact]
  public void SnapshotOfAnUnpausedRunIsEmpty() {
    NewHandle().SnapshotPause().Should().Be(new QueuePauseSnapshot(false, null, null, null));
  }

  [Fact]
  public void SnapshotReportsAnIdlePause() {
    var handle = NewHandle();
    handle.EnterIdlePause(new DateTimeOffset(2026, 9, 16, 20, 59, 6, TimeSpan.FromHours(2)), Start);

    var snapshot = handle.SnapshotPause();

    snapshot.Paused.Should().BeTrue();
    snapshot.PausedAt.Should().Be(Start);
    snapshot.Reason.Should().Be("idle pause: resumes at 20:59");
    snapshot.Kind.Should().Be(QueuePauseKinds.Idle);
  }

  [Fact]
  public void SnapshotReasonFollowsAnUpdatedResumeTime() {
    var handle = NewHandle();
    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(30), Start);
    handle.EnterIdlePause(new DateTimeOffset(2026, 9, 16, 21, 5, 0, TimeSpan.FromHours(2)), Start);

    handle.SnapshotPause().Reason.Should().Be("idle pause: resumes at 21:05");
  }

  [Fact]
  public void SnapshotIsEmptyAgainOnceTheIdlePauseEnds() {
    var handle = NewHandle();
    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(2), Start);
    handle.ClearIdlePause();

    handle.SnapshotPause().Should().Be(new QueuePauseSnapshot(false, null, null, null));
  }

  // ── Failure-policy pause and precedence (US2) ────────────────────────────────────────────────

  [Fact]
  public void SnapshotReportsAFailurePolicyPauseWithItsOwnValues() {
    var handle = NewHandle();
    handle.EnterPolicyPause("failure policy: 5 consecutive failed cycles", Start);

    var snapshot = handle.SnapshotPause();

    snapshot.Should().Be(new QueuePauseSnapshot(
      true, Start, "failure policy: 5 consecutive failed cycles", QueuePauseKinds.FailurePolicy));
  }

  [Fact]
  public void FailurePolicyPauseWinsOverAnIdlePause() {
    var handle = NewHandle();
    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(2), Start);
    var policyAt = Start + TimeSpan.FromSeconds(10);
    handle.EnterPolicyPause("failure policy: 2 consecutive failed cycles", policyAt);

    var snapshot = handle.SnapshotPause();

    snapshot.Kind.Should().Be(QueuePauseKinds.FailurePolicy);
    snapshot.PausedAt.Should().Be(policyAt);
    snapshot.Reason.Should().StartWith("failure policy:");
  }

  [Fact]
  public void ResumeDoesNotEndAnIdlePause() {
    var handle = NewHandle();
    handle.EnterIdlePause(Start + TimeSpan.FromMinutes(2), Start);

    handle.ResumeFromPolicyPause().Should().BeFalse();

    var snapshot = handle.SnapshotPause();
    snapshot.Kind.Should().Be(QueuePauseKinds.Idle);
    snapshot.PausedAt.Should().Be(Start);
  }

  [Fact]
  public void PauseKindWireValuesAreStable() {
    QueuePauseKinds.Idle.Should().Be("idle");
    QueuePauseKinds.FailurePolicy.Should().Be("failurePolicy");
  }
}
