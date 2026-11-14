using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 189: smart scheduling of a Timer booking at coordinator level.</summary>
public sealed class SelfRescheduleCoordinatorSmartTests {
  private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
  private static readonly SmartSchedulingRequest Window15 = new(15);

  private static (QueueRunHandle Handle, SelfRescheduleCoordinator Coordinator, RunStatisticsTestLogger<SelfRescheduleCoordinator> Logger) Setup() {
    var registry = new QueueRunRegistry();
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource() };
    registry.TryAdd("q1", handle);
    var clock = new FakeTimeProvider(Start);
    var logger = new RunStatisticsTestLogger<SelfRescheduleCoordinator>();
    return (handle, new SelfRescheduleCoordinator(registry, clock, logger), logger);
  }

  private static SelfRescheduleResult Book(
      SelfRescheduleCoordinator coordinator, string sequenceId, int minutes, SmartSchedulingRequest? request = null,
      SelfRescheduleKeep keep = SelfRescheduleKeep.None, string? runId = "run-1") =>
    coordinator.ScheduleSelf("q1", sequenceId, SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(minutes), null, keep, runId, request);

  private static SelfRescheduleEntry Pending(QueueRunHandle handle, string sequenceId) =>
    handle.SnapshotPendingTimerFirings().Single(f => f.SequenceId == sequenceId);

  [Fact]
  public void APendingBookingInTheWindowMovesTheBooking() {
    var (handle, coordinator, logger) = Setup();
    Book(coordinator, "seq-A", 10);

    var result = Book(coordinator, "seq-B", 0, Window15);

    result.FireAt.Should().Be(Start.AddMinutes(10));
    result.RequestedAt.Should().Be(Start);
    result.SmartTarget.Should().Be(new SmartCandidate(Start.AddMinutes(10), "seq-A", SmartTargetKind.SelfReschedule));
    result.ResolvedTiming.Should().Be(Start.AddMinutes(10).ToString("u", System.Globalization.CultureInfo.InvariantCulture));
    var entry = Pending(handle, "seq-B");
    entry.FireAt.Should().Be(Start.AddMinutes(10));
    entry.Postponement.Should().Be(new SmartPostponement(Start, "seq-A", SmartTargetKind.SelfReschedule));
    logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information)
      .Which.Message.Should().Contain("smart scheduling, window 15 min");
  }

  [Fact]
  public void APendingBookingAfterTheWindowDoesNotMoveTheBooking() {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-A", 20);

    var result = Book(coordinator, "seq-B", 0, Window15);

    result.FireAt.Should().Be(Start);
    result.RequestedAt.Should().BeNull();
    result.SmartTarget.Should().BeNull();
    Pending(handle, "seq-B").Postponement.Should().BeNull();
  }

  [Fact]
  public void APendingBookingBeforeTheRequestedTimeDoesNotMoveTheBooking() {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-A", 5);

    var result = Book(coordinator, "seq-B", 10, Window15);

    result.FireAt.Should().Be(Start.AddMinutes(10));
    Pending(handle, "seq-B").Postponement.Should().BeNull();
  }

  [Fact]
  public void ThePendingBookingOfTheSameSequenceIsNotATargetAndTheNewBookingReplacesIt() {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-B", 10);

    var result = Book(coordinator, "seq-B", 0, Window15);

    result.FireAt.Should().Be(Start);
    result.SmartTarget.Should().BeNull();
    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(Start);
  }

  [Fact]
  public void APendingLiveScheduleOfADifferentSequenceIsATarget() {
    var (handle, coordinator, _) = Setup();
    handle.PendingLiveSchedules["seq-A"] = Start.AddMinutes(7);

    var result = Book(coordinator, "seq-B", 0, Window15);

    result.FireAt.Should().Be(Start.AddMinutes(7));
    result.SmartTarget!.Kind.Should().Be(SmartTargetKind.LiveSchedule);
  }

  [Fact]
  public void ALiveScheduleOfTheSameSequenceIsNotATarget() {
    var (handle, coordinator, _) = Setup();
    handle.PendingLiveSchedules["seq-B"] = Start.AddMinutes(7);

    var result = Book(coordinator, "seq-B", 0, Window15);

    result.FireAt.Should().Be(Start);
    result.SmartTarget.Should().BeNull();
  }

  [Fact]
  public void ATemplateTimerEntryOfADifferentSequenceIsATargetAndOneOfTheSameSequenceIsNot() {
    var (handle, coordinator, _) = Setup();
    handle.Schedule = new QueueRunSchedule(new[] {
      new QueueTemplateEntry { EntryId = "own", SequenceId = "seq-B", ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromMinutes(3) },
      new QueueTemplateEntry { EntryId = "other", SequenceId = "seq-A", ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromMinutes(8) },
    }, Start, cycling: true);

    var result = Book(coordinator, "seq-B", 0, Window15);

    result.FireAt.Should().Be(Start.AddMinutes(8));
    result.SmartTarget.Should().Be(new SmartCandidate(Start.AddMinutes(8), "seq-A", SmartTargetKind.TemplateTimer));
  }

  [Fact]
  public void ANullRequestBooksTheRequestedTime() {
    var (handle, coordinator, logger) = Setup();
    Book(coordinator, "seq-A", 10);

    var result = Book(coordinator, "seq-B", 0);

    result.FireAt.Should().Be(Start);
    result.RequestedAt.Should().BeNull();
    result.SmartTarget.Should().BeNull();
    Pending(handle, "seq-B").Postponement.Should().BeNull();
    logger.Entries.Should().BeEmpty();
  }

  [Theory]
  [InlineData(SelfRescheduleOption.OncePerRun, "this cycle")]
  [InlineData(SelfRescheduleOption.AtQueueStart, "next iteration boundary")]
  [InlineData(SelfRescheduleOption.EveryStep, "after every step")]
  public void OtherOptionsWithARequestBehaveAsWithNoRequest(SelfRescheduleOption option, string timing) {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-A", 10);

    var result = coordinator.ScheduleSelf("q1", "seq-B", option, null, null, null, SelfRescheduleKeep.None, "run-1", Window15);

    result.FireAt.Should().BeNull();
    result.ResolvedTiming.Should().Be(timing);
    result.RequestedAt.Should().BeNull();
    result.SmartTarget.Should().BeNull();
    handle.SnapshotPendingTimerFirings().Should().ContainSingle();
  }

  [Fact]
  public void KeepEarliestComparesTheBookedTime() {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-A", 10);
    Book(coordinator, "seq-B", 5, keep: SelfRescheduleKeep.Earliest);

    var result = Book(coordinator, "seq-B", 0, Window15, SelfRescheduleKeep.Earliest);

    result.KeptPending.Should().BeTrue();
    result.PendingFireAt.Should().Be(Start.AddMinutes(5));
    result.RequestedAt.Should().Be(Start);
    result.SmartTarget!.SequenceId.Should().Be("seq-A");
    var pending = Pending(handle, "seq-B");
    pending.FireAt.Should().Be(Start.AddMinutes(5));
    pending.Postponement.Should().BeNull();
  }

  [Fact]
  public void ARearmedBookingKeepsItsFireTimeAndPostponement() {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-A", 10);
    Book(coordinator, "seq-B", 0, Window15);
    var drained = handle.DrainDueTimerFirings(Start.AddMinutes(10));
    var postponed = drained.Single(e => e.SequenceId == "seq-B");

    handle.RearmTimerFiring(postponed);

    var rearmed = Pending(handle, "seq-B");
    rearmed.FireAt.Should().Be(Start.AddMinutes(10));
    rearmed.Postponement.Should().Be(new SmartPostponement(Start, "seq-A", SmartTargetKind.SelfReschedule));
  }

  [Fact]
  public void TheTargetBookingKeepsItsTime() {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-A", 10);

    Book(coordinator, "seq-B", 0, Window15);

    var target = Pending(handle, "seq-A");
    target.FireAt.Should().Be(Start.AddMinutes(10));
    target.Postponement.Should().BeNull();
  }

  [Fact]
  public void AHandleWithNoScheduleGivesNoTemplateCandidateAndDoesNotThrow() {
    var (handle, coordinator, _) = Setup();
    handle.Schedule.Should().BeNull();

    var act = () => Book(coordinator, "seq-B", 0, Window15);

    act.Should().NotThrow().Which.FireAt.Should().Be(Start);
  }

  [Fact]
  public void APostponedBookingKeepsItsTimeWhenTheTargetIsCancelledOrMoved() {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-A", 10);
    Book(coordinator, "seq-B", 0, Window15);

    coordinator.CancelSelf("q1", "seq-A");

    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.SequenceId.Should().Be("seq-B");
    var entry = Pending(handle, "seq-B");
    entry.FireAt.Should().Be(Start.AddMinutes(10));
    entry.Postponement.Should().NotBeNull();

    Book(coordinator, "seq-A", 40);

    entry = Pending(handle, "seq-B");
    entry.FireAt.Should().Be(Start.AddMinutes(10));
    entry.Postponement!.TargetSequenceId.Should().Be("seq-A");
  }

  [Fact]
  public void ATimeOfDayBookingMovesToATargetInTheWindow() {
    var (handle, coordinator, _) = Setup();
    Book(coordinator, "seq-A", 12);

    var result = coordinator.ScheduleSelf("q1", "seq-B", SelfRescheduleOption.Timer, new TimeOnly(12, 5), null, null,
      SelfRescheduleKeep.None, "run-1", Window15);

    result.FireAt.Should().Be(Start.AddMinutes(12));
    result.RequestedAt.Should().Be(Start.AddMinutes(5));
    Pending(handle, "seq-B").Postponement!.RequestedAt.Should().Be(Start.AddMinutes(5));
  }
}
