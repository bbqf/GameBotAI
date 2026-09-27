using System;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 106 (data-model section 9): the fault cycle of a device fault episode.</summary>
public sealed class QueueCycleLedgerFaultCycleTests {
  private static readonly DateTimeOffset T0 = new(2026, 9, 25, 3, 0, 0, TimeSpan.FromHours(2));

  [Fact]
  public void RecordFaultCycleSealsOneFailedCycleWithNoEntries() {
    var ledger = new QueueCycleLedger();

    ledger.RecordFaultCycle(T0, T0.AddMinutes(2));

    var cycles = ledger.SnapshotCycles(10);
    cycles.Should().ContainSingle();
    cycles[0].Succeeded.Should().BeFalse();
    cycles[0].Entries.Should().BeEmpty();
    cycles[0].StartedAt.Should().Be(T0);
    cycles[0].CompletedAt.Should().Be(T0.AddMinutes(2));
    var health = ledger.SnapshotHealth();
    health.CyclesCompleted.Should().Be(1);
    health.LastCycleSucceeded.Should().BeFalse();
  }

  [Fact]
  public void RecordFaultCycleIncrementsTheConsecutiveFailures() {
    var ledger = new QueueCycleLedger();
    ledger.EnsureOpen(T0);
    ledger.RecordEntry("A", false);
    ledger.CompleteOpen(T0.AddMinutes(1));

    ledger.RecordFaultCycle(T0.AddMinutes(1), T0.AddMinutes(3));

    ledger.SnapshotHealth().ConsecutiveFailedCycles.Should().Be(2);
  }

  [Fact]
  public void RecordFaultCycleDoesNotChangeTheOpenCycle() {
    var ledger = new QueueCycleLedger();
    ledger.EnsureOpen(T0);
    ledger.RecordEntry("A", true);

    ledger.RecordFaultCycle(T0, T0.AddMinutes(2));
    ledger.CompleteOpen(T0.AddMinutes(3));

    var newest = ledger.SnapshotCycles(1)[0];
    newest.StartedAt.Should().Be(T0);
    newest.Entries.Should().ContainSingle().Which.SequenceId.Should().Be("A");
    newest.Succeeded.Should().BeTrue();
    ledger.SnapshotHealth().CyclesCompleted.Should().Be(2);
  }
}
