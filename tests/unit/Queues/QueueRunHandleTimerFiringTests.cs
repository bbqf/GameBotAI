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

  // ── Feature 092: pending self-reschedule work opens the run's scheduling loop (#198) ──────────

  [Fact]
  public void FreshHandleHasNoPendingSelfRescheduleWork() {
    NewHandle().HasPendingSelfRescheduleWork.Should().BeFalse();
  }

  [Fact]
  public void PendingTimerFiringIsPendingSelfRescheduleWork() {
    var handle = NewHandle();
    handle.AddTimerFiring(Timer("seq-A", T1));
    handle.HasPendingSelfRescheduleWork.Should().BeTrue();
  }

  [Fact]
  public void PendingNextCycleStartIsPendingSelfRescheduleWork() {
    var handle = NewHandle();
    handle.PendingNextCycleStart.Enqueue(new("id", "seq-A", SelfRescheduleOption.AtQueueStart, null));
    handle.HasPendingSelfRescheduleWork.Should().BeTrue();
  }

  [Fact]
  public void PendingOncePerRunIsPendingSelfRescheduleWork() {
    var handle = NewHandle();
    handle.PendingOncePerRun.Enqueue(new("id", "seq-A", SelfRescheduleOption.OncePerRun, null));
    handle.HasPendingSelfRescheduleWork.Should().BeTrue();
  }

  [Fact]
  public void PendingLiveScheduleIsPendingSelfRescheduleWork() {
    var handle = NewHandle();
    handle.PendingLiveSchedules["seq-A"] = T1;
    handle.HasPendingSelfRescheduleWork.Should().BeTrue();
  }

  [Fact]
  public void EveryStepInjectionAloneIsNotPendingSelfRescheduleWork() {
    var handle = NewHandle();
    handle.EveryStepInjections["seq-A"] = new("id", "seq-A", SelfRescheduleOption.EveryStep, null);
    handle.HasPendingSelfRescheduleWork.Should().BeFalse();
  }

  [Fact] // T017 (feature 116) — edge case "liveness gate": a held Timer booking keeps its scope. Regression guard.
  public void HeldTimerFiringKeepsItsScope() {
    var handle = NewHandle();
    var scope = GameBot.Domain.Parameters.ParameterScope.Empty.Child(
      GameBot.Domain.Parameters.ParameterScopeLayers.Entry,
      new[] { new GameBot.Domain.Parameters.ParameterBinding { Name = "slot", Value = "one" } },
      null);
    handle.AddTimerFiring(Timer("seq-A", T1) with { Scope = scope });

    var drained = handle.DrainDueTimerFirings(T1).Should().ContainSingle().Subject;
    handle.RearmTimerFiring(drained);
    var again = handle.DrainDueTimerFirings(T1).Should().ContainSingle().Subject;

    again.Scope.Should().BeSameAs(scope);
  }

  // ── Feature 125: keep earliest (decision table of data-model.md) ──────────────────────────────

  private static SelfRescheduleEntry Booking(string sequenceId, DateTimeOffset fireAt, string? runId) =>
    Timer(sequenceId, fireAt) with { RunId = runId };

  private static readonly DateTimeOffset Early = new(2026, 1, 1, 12, 15, 0, TimeSpan.Zero);
  private static readonly DateTimeOffset Late = new(2026, 1, 1, 12, 50, 0, TimeSpan.Zero);

  [Fact]
  public void NoKeepAddsWhenNothingIsPending() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Late, "r1"), keepEarliest: false).Kind.Should().Be(TimerBookingKind.Added);
  }

  [Fact]
  public void NoKeepReplacesAlsoForTheSameRunAndAnEarlierPendingTime() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Early, "r1"), keepEarliest: false);

    handle.AddTimerFiring(Booking("seq-A", Late, "r1"), keepEarliest: false).Kind.Should().Be(TimerBookingKind.Replaced);

    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(Late);
  }

  [Fact]
  public void KeepAddsWhenNothingIsPending() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Late, "r1"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.Added);
  }

  [Fact]
  public void KeepReplacesAnEarlierBookingOfAnotherRun() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Early, "r1"), keepEarliest: false);

    handle.AddTimerFiring(Booking("seq-A", Late, "r2"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.Replaced);

    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(Late);
  }

  [Fact]
  public void KeepReplacesAnEarlierBookingWithNoRunId() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Early, null), keepEarliest: false);

    handle.AddTimerFiring(Booking("seq-A", Late, "r1"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.Replaced);

    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(Late);
  }

  [Fact]
  public void KeepWithNoRunIdReplacesEvenWhenThePendingBookingHasNoRunId() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Early, null), keepEarliest: false);

    handle.AddTimerFiring(Booking("seq-A", Late, null), keepEarliest: true).Kind.Should().Be(TimerBookingKind.Replaced);
  }

  [Fact]
  public void KeepReplacesWhenTheSameRunBooksAnEarlierTime() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Late, "r1"), keepEarliest: true);

    handle.AddTimerFiring(Booking("seq-A", Early, "r1"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.Replaced);

    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(Early);
  }

  [Fact]
  public void KeepKeepsThePendingBookingWhenTheSameRunBooksALaterTime() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Early, "r1"), keepEarliest: true);

    var result = handle.AddTimerFiring(Booking("seq-A", Late, "r1"), keepEarliest: true);

    result.Kind.Should().Be(TimerBookingKind.KeptPending);
    result.PendingFireAt.Should().Be(Early);
    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(Early);
  }

  [Fact]
  public void KeepKeepsThePendingBookingWhenTheTimesAreEqual() {
    var handle = NewHandle();
    var first = Booking("seq-A", Early, "r1");
    handle.AddTimerFiring(first, keepEarliest: true);

    handle.AddTimerFiring(Booking("seq-A", Early, "r1"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.KeptPending);

    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.Id.Should().Be(first.Id);
  }

  [Fact]
  public void APastFireTimeBeatsAFutureFireTime() {
    var handle = NewHandle();
    var past = T1.AddHours(-1);
    handle.AddTimerFiring(Booking("seq-A", T1, "r1"), keepEarliest: true);

    handle.AddTimerFiring(Booking("seq-A", past, "r1"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.Replaced);
    handle.AddTimerFiring(Booking("seq-A", T1, "r1"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.KeptPending);

    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(past);
  }

  [Fact]
  public void ABookingOfOneSequenceNeverChangesTheBookingOfAnotherSequence() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Early, "r1"), keepEarliest: true);
    handle.AddTimerFiring(Booking("seq-B", Late, "r1"), keepEarliest: true);

    handle.AddTimerFiring(Booking("seq-A", Late, "r1"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.KeptPending);

    var pending = handle.SnapshotPendingTimerFirings();
    pending.Should().HaveCount(2);
    pending.Single(f => f.SequenceId == "seq-B").FireAt.Should().Be(Late);
    pending.Single(f => f.SequenceId == "seq-A").FireAt.Should().Be(Early);
  }

  // ── Feature 125: the re-arm path drops the run id ────────────────────────────────────────────

  [Fact]
  public void RearmStoresTheEntryWithNoRunIdAndTheSameFireTime() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", T1, "r1"));
    var drained = handle.DrainDueTimerFirings(T1).Should().ContainSingle().Subject;

    handle.RearmTimerFiring(drained);

    var again = handle.SnapshotPendingTimerFirings().Should().ContainSingle().Subject;
    again.RunId.Should().BeNull();
    again.FireAt.Should().Be(T1);
  }

  [Fact]
  public void RearmAddsOnlyWhenNoTimerBookingExistsForTheSequence() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", T2, "r2"));

    handle.RearmTimerFiring(Booking("seq-A", T1, "r1"));

    var pending = handle.SnapshotPendingTimerFirings().Should().ContainSingle().Subject;
    pending.FireAt.Should().Be(T2);
    pending.RunId.Should().Be("r2");
  }

  [Fact]
  public void AKeepBookingWithALaterTimeReplacesARearmedBooking() {
    var handle = NewHandle();
    handle.AddTimerFiring(Booking("seq-A", Early, "r1"));
    handle.RearmTimerFiring(handle.DrainDueTimerFirings(Early).Single());

    handle.AddTimerFiring(Booking("seq-A", Late, "r1"), keepEarliest: true).Kind.Should().Be(TimerBookingKind.Replaced);

    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(Late);
  }
}
