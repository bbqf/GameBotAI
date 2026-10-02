using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 125: the keep-earliest rule of Timer bookings at coordinator level.</summary>
public sealed class SelfRescheduleCoordinatorKeepTests {
  private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

  private static (QueueRunHandle Handle, SelfRescheduleCoordinator Coordinator, FakeTimeProvider Clock, RunStatisticsTestLogger<SelfRescheduleCoordinator> Logger) Setup() {
    var registry = new QueueRunRegistry();
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource() };
    registry.TryAdd("q1", handle);
    var clock = new FakeTimeProvider(Start);
    var logger = new RunStatisticsTestLogger<SelfRescheduleCoordinator>();
    return (handle, new SelfRescheduleCoordinator(registry, clock, logger), clock, logger);
  }

  private static SelfRescheduleResult Book(
      SelfRescheduleCoordinator coordinator, int minutes, SelfRescheduleKeep keep, string? runId = "run-1", string sequenceId = "seq-A") =>
    coordinator.ScheduleSelf("q1", sequenceId, SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(minutes), null, keep, runId);

  [Fact]
  public void FifteenFiftyThirtyFortyWithKeepLeavesFifteen() {
    var (handle, coordinator, _, _) = Setup();

    foreach (var minutes in new[] { 15, 50, 30, 40 }) {
      Book(coordinator, minutes, SelfRescheduleKeep.Earliest);
    }

    handle.SnapshotPendingTimerFirings().Should().ContainSingle()
      .Which.FireAt.Should().Be(Start.AddMinutes(15));
  }

  [Fact]
  public void ALaterBookingReturnsScheduledWithKeptPendingAndThePendingFireTime() {
    var (_, coordinator, _, _) = Setup();
    Book(coordinator, 15, SelfRescheduleKeep.Earliest);

    var result = Book(coordinator, 50, SelfRescheduleKeep.Earliest);

    result.Outcome.Should().Be(SelfRescheduleOutcome.Scheduled);
    result.KeptPending.Should().BeTrue();
    result.PendingFireAt.Should().Be(Start.AddMinutes(15));
  }

  [Fact]
  public void AnEarlierBookingReplacesThePendingBooking() {
    var (handle, coordinator, _, _) = Setup();
    Book(coordinator, 50, SelfRescheduleKeep.Earliest);

    var result = Book(coordinator, 15, SelfRescheduleKeep.Earliest);

    result.KeptPending.Should().BeFalse();
    handle.SnapshotPendingTimerFirings().Should().ContainSingle()
      .Which.FireAt.Should().Be(Start.AddMinutes(15));
  }

  [Fact]
  public void AnEqualBookingIsKeptBack() {
    var (handle, coordinator, _, _) = Setup();
    var first = Book(coordinator, 15, SelfRescheduleKeep.Earliest);

    var result = Book(coordinator, 15, SelfRescheduleKeep.Earliest);

    result.KeptPending.Should().BeTrue();
    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.Id.Should().Be(first.EntryId);
  }

  [Fact]
  public void ABookingInThePastReplacesAPendingFutureBookingAndALaterFutureBookingDoesNotReplaceIt() {
    var (handle, coordinator, _, _) = Setup();
    Book(coordinator, 30, SelfRescheduleKeep.Earliest);

    var past = Book(coordinator, -5, SelfRescheduleKeep.Earliest);
    var future = Book(coordinator, 10, SelfRescheduleKeep.Earliest);

    past.KeptPending.Should().BeFalse();
    future.KeptPending.Should().BeTrue();
    handle.SnapshotPendingTimerFirings().Should().ContainSingle()
      .Which.FireAt.Should().Be(Start.AddMinutes(-5));
  }

  [Fact]
  public void ABookingWithoutKeepRecordsTheRunSoALaterKeepBookingOfTheSameRunComparesWithIt() {
    var (handle, coordinator, _, _) = Setup();
    Book(coordinator, 15, SelfRescheduleKeep.None);

    var result = Book(coordinator, 50, SelfRescheduleKeep.Earliest);

    result.KeptPending.Should().BeTrue();
    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.RunId.Should().Be("run-1");
  }

  [Fact]
  public void ABookingOfAnotherRunReplacesTheEarlierPendingBooking() {
    var (handle, coordinator, _, _) = Setup();
    Book(coordinator, 15, SelfRescheduleKeep.Earliest, "run-1");

    var result = Book(coordinator, 50, SelfRescheduleKeep.Earliest, "run-2");

    result.KeptPending.Should().BeFalse();
    handle.SnapshotPendingTimerFirings().Should().ContainSingle()
      .Which.FireAt.Should().Be(Start.AddMinutes(50));
  }

  [Fact]
  public void ALosingBookingWritesOneInformationMessageWithBothFireTimes() {
    var (_, coordinator, _, logger) = Setup();
    Book(coordinator, 15, SelfRescheduleKeep.Earliest);

    var result = Book(coordinator, 50, SelfRescheduleKeep.Earliest);

    result.Outcome.Should().Be(SelfRescheduleOutcome.Scheduled);
    var entry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information).Subject;
    entry.Message.Should().Contain(Start.AddMinutes(15).ToString("u", System.Globalization.CultureInfo.InvariantCulture));
    entry.Message.Should().Contain(Start.AddMinutes(50).ToString("u", System.Globalization.CultureInfo.InvariantCulture));
    logger.WarningCount.Should().Be(0);
  }

  [Fact]
  public void ABookingThatWinsWritesNoMessage() {
    var (_, coordinator, _, logger) = Setup();

    Book(coordinator, 15, SelfRescheduleKeep.Earliest);
    Book(coordinator, 10, SelfRescheduleKeep.Earliest);

    logger.Entries.Should().BeEmpty();
  }

  [Fact]
  public void ADifferentSequenceIsNotAffected() {
    var (handle, coordinator, _, _) = Setup();
    Book(coordinator, 15, SelfRescheduleKeep.Earliest, sequenceId: "seq-A");

    var result = Book(coordinator, 50, SelfRescheduleKeep.Earliest, sequenceId: "seq-B");

    result.KeptPending.Should().BeFalse();
    handle.SnapshotPendingTimerFirings().Select(f => f.SequenceId).Should().BeEquivalentTo(new[] { "seq-A", "seq-B" });
  }
}
