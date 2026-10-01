using System;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 123: <c>CancelSelf</c> on the coordinator.</summary>
public sealed class SelfRescheduleCoordinatorCancelTests {
  private static (QueueRunHandle Handle, SelfRescheduleCoordinator Coordinator) Setup() {
    var registry = new QueueRunRegistry();
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource() };
    registry.TryAdd("q1", handle);
    return (handle, new SelfRescheduleCoordinator(registry));
  }

  [Fact]
  public void CancelsBookingsAndReportsTheCount() {
    var (handle, coordinator) = Setup();
    coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.OncePerRun, null, null);
    coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(5));
    coordinator.ScheduleSelf("q1", "B", SelfRescheduleOption.OncePerRun, null, null);

    var result = coordinator.CancelSelf("q1", "A");

    result.Outcome.Should().Be(SelfRescheduleCancelOutcome.Cancelled);
    result.RemovedCount.Should().Be(2);
    handle.PendingOncePerRun.Should().ContainSingle().Which.SequenceId.Should().Be("B");
  }

  [Fact]
  public void ReportsNothingPendingWhenThereIsNoBooking() {
    var (_, coordinator) = Setup();

    var result = coordinator.CancelSelf("q1", "A");

    result.Outcome.Should().Be(SelfRescheduleCancelOutcome.NothingPending);
    result.RemovedCount.Should().Be(0);
  }

  [Fact]
  public void ReportsNotRunningWhenTheRunIsNotActive() {
    var coordinator = new SelfRescheduleCoordinator(new QueueRunRegistry());

    coordinator.CancelSelf("missing", "A").Outcome.Should().Be(SelfRescheduleCancelOutcome.NotRunning);
  }

  [Fact]
  public void ScheduleSelfStillRejectsCancel() {
    var (_, coordinator) = Setup();

    var act = () => coordinator.ScheduleSelf("q1", "A", SelfRescheduleOption.Cancel, null, null);

    act.Should().Throw<ArgumentOutOfRangeException>();
  }
}
