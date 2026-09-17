using System;
using System.Linq;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.UnitTests;

/// <summary>
/// Semantics of the run-scoped cycle ledger (feature 086, issue #180) — the state that lets a cycling
/// queue be told apart from a stalled one without stopping it.
/// </summary>
public class QueueCycleLedgerTests {
  private static readonly DateTimeOffset T0 =
    new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2));

  private static DateTimeOffset At(int minutes) => T0.AddMinutes(minutes);

  /// <summary>Runs one complete cycle whose entries have the given outcomes.</summary>
  private static void RunCycle(QueueCycleLedger ledger, int startMinute, params bool[] outcomes) {
    ledger.EnsureOpen(At(startMinute));
    for (var i = 0; i < outcomes.Length; i++) {
      ledger.RecordEntry($"seq-{i}", outcomes[i]);
    }
    ledger.CompleteOpen(At(startMinute + 1));
  }

  [Fact]
  public void NewLedger_ReportsNoCompletedCycles() {
    var health = new QueueCycleLedger().SnapshotHealth();

    health.CyclesCompleted.Should().Be(0);
    health.ConsecutiveFailedCycles.Should().Be(0);
    health.LastCycleStartedAt.Should().BeNull();
    health.LastCycleCompletedAt.Should().BeNull();
    health.LastCycleSucceeded.Should().BeNull();
    health.CurrentEntryIndex.Should().BeNull();
  }

  [Fact] // feature 093 (FR-007): an empty open cycle is dropped, so the next cycle starts fresh.
  public void DiscardOpenIfEmpty_DropsEntrylessOpenCycle() {
    var ledger = new QueueCycleLedger();

    ledger.EnsureOpen(At(0));
    ledger.DiscardOpenIfEmpty();
    ledger.EnsureOpen(At(30));
    ledger.RecordEntry("seq", true);
    ledger.CompleteOpen(At(31));

    var cycle = ledger.SnapshotCycles(10).Single();
    cycle.StartedAt.Should().Be(At(30));
    ledger.SnapshotHealth().CyclesCompleted.Should().Be(1);
  }

  [Fact]
  public void DiscardOpenIfEmpty_KeepsOpenCycleWithEntries() {
    var ledger = new QueueCycleLedger();

    ledger.EnsureOpen(At(0));
    ledger.RecordEntry("seq", false);
    ledger.DiscardOpenIfEmpty();
    ledger.EnsureOpen(At(5));
    ledger.CompleteOpen(At(6));

    var cycle = ledger.SnapshotCycles(10).Single();
    cycle.StartedAt.Should().Be(At(0));
    cycle.Entries.Select(e => e.SequenceId).Should().Equal("seq");
  }

  [Fact]
  public void DiscardOpenIfEmpty_WithNoOpenCycle_ChangesNothing() {
    var ledger = new QueueCycleLedger();
    RunCycle(ledger, 0, false);

    ledger.DiscardOpenIfEmpty();

    var health = ledger.SnapshotHealth();
    health.CyclesCompleted.Should().Be(1);
    health.ConsecutiveFailedCycles.Should().Be(1);
  }

  [Fact]
  public void CompletedCycle_RecordsOrdinalInstantsAndEntries() {
    var ledger = new QueueCycleLedger();

    RunCycle(ledger, 0, true, true);

    var cycle = ledger.SnapshotCycles(10).Single();
    cycle.Ordinal.Should().Be(1);
    cycle.StartedAt.Should().Be(At(0));
    cycle.CompletedAt.Should().Be(At(1));
    cycle.Succeeded.Should().BeTrue();
    cycle.Entries.Select(e => e.SequenceId).Should().Equal("seq-0", "seq-1");
  }

  [Fact]
  public void Ordinals_AreOneBasedAndSequential() {
    var ledger = new QueueCycleLedger();

    RunCycle(ledger, 0, true);
    RunCycle(ledger, 10, true);
    RunCycle(ledger, 20, true);

    ledger.SnapshotCycles(10).Select(c => c.Ordinal).Should().Equal(3, 2, 1);
    ledger.SnapshotHealth().CyclesCompleted.Should().Be(3);
  }

  [Fact]
  public void EnsureOpen_IsIdempotent_SoRepeatedCallsDoNotRestartTheCycle() {
    var ledger = new QueueCycleLedger();

    ledger.EnsureOpen(At(0));
    ledger.RecordEntry("seq-a", true);
    ledger.EnsureOpen(At(5));      // second call in the same cycle must not reset anything
    ledger.RecordEntry("seq-b", true);
    ledger.CompleteOpen(At(9));

    var cycle = ledger.SnapshotCycles(10).Single();
    cycle.StartedAt.Should().Be(At(0), "the first EnsureOpen defines the cycle start");
    cycle.Entries.Should().HaveCount(2, "entries from both calls belong to the one open cycle");
  }

  [Fact]
  public void Cycle_FailsIfAnyEntryFailed() {
    var ledger = new QueueCycleLedger();

    RunCycle(ledger, 0, true, false, true);

    ledger.SnapshotCycles(10).Single().Succeeded.Should().BeFalse();
    ledger.SnapshotHealth().LastCycleSucceeded.Should().BeFalse();
  }

  [Fact]
  public void CycleWithNoEntries_Succeeds() {
    var ledger = new QueueCycleLedger();

    RunCycle(ledger, 0);

    ledger.SnapshotCycles(10).Single().Succeeded.Should()
      .BeTrue("an idle-but-alive queue is not a failing one");
  }

  [Fact]
  public void RecordEmptyCycle_CountsAsACompletedCycle() {
    var ledger = new QueueCycleLedger();

    ledger.RecordEmptyCycle(At(0));

    ledger.SnapshotHealth().CyclesCompleted.Should().Be(1);
    ledger.SnapshotCycles(10).Single().Entries.Should().BeEmpty();
  }

  [Fact]
  public void ConsecutiveFailures_ClimbThenResetOnSuccess() {
    var ledger = new QueueCycleLedger();

    RunCycle(ledger, 0, false);
    RunCycle(ledger, 10, false);
    RunCycle(ledger, 20, false);
    ledger.SnapshotHealth().ConsecutiveFailedCycles.Should().Be(3);

    RunCycle(ledger, 30, true);

    var health = ledger.SnapshotHealth();
    health.ConsecutiveFailedCycles.Should().Be(0, "a successful cycle clears the streak");
    health.LastCycleSucceeded.Should().BeTrue();
    health.CyclesCompleted.Should().Be(4, "the total keeps counting regardless of outcome");
  }

  [Fact]
  public void OpenCycle_IsNeverPublishedUntilItCompletes() {
    var ledger = new QueueCycleLedger();

    ledger.EnsureOpen(At(0));
    ledger.RecordEntry("seq-a", true);

    ledger.SnapshotCycles(10).Should().BeEmpty("a cycle interrupted by a stop is never published");
    ledger.SnapshotHealth().CyclesCompleted.Should().Be(0);
  }

  [Fact]
  public void CompleteOpen_WithNoOpenCycle_IsANoOp() {
    var ledger = new QueueCycleLedger();

    ledger.CompleteOpen(At(1));

    ledger.SnapshotHealth().CyclesCompleted.Should().Be(0);
    ledger.SnapshotCycles(10).Should().BeEmpty();
  }

  [Fact]
  public void RecordEntry_WithNoOpenCycle_IsANoOp() {
    var ledger = new QueueCycleLedger();

    ledger.RecordEntry("seq-a", false);
    RunCycle(ledger, 0, true);

    ledger.SnapshotCycles(10).Single().Entries.Should()
      .ContainSingle("the stray entry belonged to no cycle and was dropped");
  }

  [Fact]
  public void CurrentEntryIndex_IsSetAndCleared() {
    var ledger = new QueueCycleLedger();

    ledger.SetCurrentEntryIndex(2);
    ledger.SnapshotHealth().CurrentEntryIndex.Should().Be(2);

    ledger.ClearCurrentEntryIndex();
    ledger.SnapshotHealth().CurrentEntryIndex.Should().BeNull("it reads as absent between entries");
  }

  [Fact]
  public void Retention_IsBounded_AndOrdinalsSurviveEviction() {
    var ledger = new QueueCycleLedger();
    var total = QueueCycleLedger.MaxRetainedCycles + 20;

    for (var i = 0; i < total; i++) RunCycle(ledger, i * 10, true);

    var cycles = ledger.SnapshotCycles(QueueCycleLedger.MaxRetainedCycles);
    cycles.Should().HaveCount(QueueCycleLedger.MaxRetainedCycles, "memory is bounded for a week-long run");
    cycles[0].Ordinal.Should().Be(total, "ordinals keep counting past the retention bound");
    cycles[^1].Ordinal.Should().Be(total - QueueCycleLedger.MaxRetainedCycles + 1);
    ledger.SnapshotHealth().CyclesCompleted.Should().Be(total, "the total is not capped by retention");
  }

  [Fact]
  public void SnapshotCycles_ReturnsNewestFirst_AtMostTheLimit() {
    var ledger = new QueueCycleLedger();
    for (var i = 0; i < 10; i++) RunCycle(ledger, i * 10, true);

    var cycles = ledger.SnapshotCycles(3);

    cycles.Should().HaveCount(3);
    cycles.Select(c => c.Ordinal).Should().Equal(10, 9, 8);
  }

  [Theory]
  [InlineData(0, 1)]
  [InlineData(-5, 1)]
  [InlineData(int.MinValue, 1)]
  [InlineData(7, 7)]
  [InlineData(9999, QueueCycleLedger.MaxRetainedCycles)]
  [InlineData(int.MaxValue, QueueCycleLedger.MaxRetainedCycles)]
  public void ClampLimit_ClampsRatherThanFailing(int requested, int expected) =>
    QueueCycleLedger.ClampLimit(requested).Should().Be(expected);

  /// <summary>
  /// A non-cycling run does its roster pass on the first loop iteration, then keeps iterating only to
  /// wait for pending relative/live timers — those later iterations never reach the pass, so the cycle
  /// they open is never completed. The run must still report exactly one cycle (FR-019), matching the
  /// engine's own counter.
  /// </summary>
  [Fact]
  public void NonCyclingRunShape_ReportsExactlyOneCycle() {
    var ledger = new QueueCycleLedger();

    // Iteration 1: the roster pass runs and completes the cycle.
    ledger.EnsureOpen(At(0));
    ledger.RecordEntry("seq-spine", true);
    ledger.CompleteOpen(At(1));

    // Iterations 2..4: timer-wait polls. They open a cycle and may fire timers, but never complete.
    for (var i = 2; i <= 4; i++) {
      ledger.EnsureOpen(At(i * 10));
      ledger.RecordEntry("seq-timer", true);
    }

    ledger.SnapshotHealth().CyclesCompleted.Should().Be(1);
    ledger.SnapshotCycles(10).Should().ContainSingle()
      .Which.Entries.Should().ContainSingle().Which.SequenceId.Should().Be("seq-spine");
  }

  /// <summary>
  /// The 2026-09-12 condition (SC-003): the loop is alive and cycling, so a start/stop flag says
  /// "Running", while every cycle fails. The ledger is what makes those two states distinguishable.
  /// </summary>
  [Fact]
  public void StalledButRunningQueue_IsDistinguishableFromAHealthyOne() {
    var healthy = new QueueCycleLedger();
    var stalled = new QueueCycleLedger();

    for (var i = 0; i < 12; i++) {
      RunCycle(healthy, i * 10, true, true);
      RunCycle(stalled, i * 10, false, false);
    }

    var h = healthy.SnapshotHealth();
    var s = stalled.SnapshotHealth();

    // Both have cycled the same number of times — the loop is running in both cases.
    s.CyclesCompleted.Should().Be(h.CyclesCompleted);

    // But two independent values separate them (SC-002).
    s.ConsecutiveFailedCycles.Should().Be(12);
    h.ConsecutiveFailedCycles.Should().Be(0);
    s.LastCycleSucceeded.Should().BeFalse();
    h.LastCycleSucceeded.Should().BeTrue();

    // And the recorded cycles name which entry is failing, without stopping anything (SC-004).
    stalled.SnapshotCycles(3).Should().OnlyContain(c => !c.Succeeded);
    stalled.SnapshotCycles(1).Single().Entries.Should().OnlyContain(e => !e.Succeeded);
  }

  [Fact]
  public void LastCycleValues_TrackTheMostRecentCompletedCycle() {
    var ledger = new QueueCycleLedger();

    RunCycle(ledger, 0, true);
    RunCycle(ledger, 30, false);

    var health = ledger.SnapshotHealth();
    health.LastCycleStartedAt.Should().Be(At(30));
    health.LastCycleCompletedAt.Should().Be(At(31));
    health.LastCycleSucceeded.Should().BeFalse();
  }
}
