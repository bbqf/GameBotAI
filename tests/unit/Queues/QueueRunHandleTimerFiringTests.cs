using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 075: the Timer self-reschedule register (<see cref="QueueRunHandle.AddTimerFiring"/>) is
/// most-recent-wins per sequence, so a self-rescheduling sequence never stacks duplicate future
/// firings. Distinct sequences remain independent, and other registers are unaffected.
/// </summary>
public sealed class QueueRunHandleTimerFiringTests {
  private static readonly DateTimeOffset T1 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
  private static readonly DateTimeOffset T2 = new(2026, 1, 1, 12, 30, 0, TimeSpan.Zero);

  private static QueueRunHandle NewHandle() =>
    new() { QueueId = "q1", Cts = new CancellationTokenSource() };

  private static SelfRescheduleEntry Timer(string sequenceId, DateTimeOffset fireAt) =>
    new(Guid.NewGuid().ToString("n"), sequenceId, SelfRescheduleOption.Timer, fireAt);

  [Fact] // T007 — FR-007: a single add produces exactly one pending firing (unchanged first-request behavior).
  public void SingleAddYieldsOneFiring() {
    var handle = NewHandle();

    handle.AddTimerFiring(Timer("seq-A", T1));

    handle.SnapshotPendingTimerFirings().Should().ContainSingle()
      .Which.SequenceId.Should().Be("seq-A");
  }

  [Fact] // T002 — FR-002/FR-006 (contract C1): a second Timer firing for the same sequence replaces the first, keeping the newest time.
  public void SecondFiringForSameSequenceReplacesFirst() {
    var handle = NewHandle();

    handle.AddTimerFiring(Timer("seq-A", T1));
    handle.AddTimerFiring(Timer("seq-A", T2));

    var pending = handle.SnapshotPendingTimerFirings();
    pending.Should().ContainSingle();
    pending[0].SequenceId.Should().Be("seq-A");
    pending[0].FireAt.Should().Be(T2);
  }

  [Fact] // T007 — FR-003 (contract C2): distinct sequences each keep their own firing.
  public void DifferentSequencesAreIndependent() {
    var handle = NewHandle();

    handle.AddTimerFiring(Timer("seq-A", T1));
    handle.AddTimerFiring(Timer("seq-B", T2));

    handle.SnapshotPendingTimerFirings().Select(f => f.SequenceId)
      .Should().BeEquivalentTo(new[] { "seq-A", "seq-B" });
  }

  [Fact] // T007 — FR-003 (contract C2): replacing seq-A leaves seq-B untouched and unreordered.
  public void ReplacingOneSequenceLeavesOthersUntouched() {
    var handle = NewHandle();
    handle.AddTimerFiring(Timer("seq-A", T1));
    handle.AddTimerFiring(Timer("seq-B", T1));

    handle.AddTimerFiring(Timer("seq-A", T2));

    var pending = handle.SnapshotPendingTimerFirings();
    pending.Should().HaveCount(2);
    pending.Single(f => f.SequenceId == "seq-B").FireAt.Should().Be(T1);
    pending.Single(f => f.SequenceId == "seq-A").FireAt.Should().Be(T2);
  }

  [Fact] // T002 — after replacement, only the newest firing is drained (the stale one is gone).
  public void DrainReturnsOnlyTheRetainedFiring() {
    var handle = NewHandle();
    handle.AddTimerFiring(Timer("seq-A", T1));
    handle.AddTimerFiring(Timer("seq-A", T2));

    // At T1 the stale firing would have been due, but it was replaced — nothing drains yet.
    handle.DrainDueTimerFirings(T1).Should().BeEmpty();
    handle.DrainDueTimerFirings(T2).Should().ContainSingle()
      .Which.FireAt.Should().Be(T2);
  }
}
