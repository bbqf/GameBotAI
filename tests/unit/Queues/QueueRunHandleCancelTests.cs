using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 123: removal of pending bookings from a queue run handle.</summary>
public sealed class QueueRunHandleCancelTests {
  private static QueueRunHandle NewHandle() => new() { QueueId = "q1", Cts = new CancellationTokenSource() };

  private static SelfRescheduleEntry Entry(string id, string sequenceId, SelfRescheduleOption option, DateTimeOffset? fireAt = null) =>
    new(id, sequenceId, option, fireAt);

  [Fact]
  public void RemovesTimerOncePerRunAndNextCycleEntriesOfTheSequence() {
    var handle = NewHandle();
    handle.AddTimerFiring(Entry("t1", "A", SelfRescheduleOption.Timer, DateTimeOffset.UtcNow.AddMinutes(5)));
    handle.PendingOncePerRun.Enqueue(Entry("o1", "A", SelfRescheduleOption.OncePerRun));
    handle.PendingNextCycleStart.Enqueue(Entry("n1", "A", SelfRescheduleOption.AtQueueStart));

    var removed = handle.RemovePendingBookings("A");

    removed.Should().Be(3);
    handle.HasPendingTimerFirings.Should().BeFalse();
    handle.PendingOncePerRun.Should().BeEmpty();
    handle.PendingNextCycleStart.Should().BeEmpty();
  }

  [Fact]
  public void KeepsEntriesOfOtherSequencesInOrder() {
    var handle = NewHandle();
    handle.PendingOncePerRun.Enqueue(Entry("o1", "B", SelfRescheduleOption.OncePerRun));
    handle.PendingOncePerRun.Enqueue(Entry("o2", "A", SelfRescheduleOption.OncePerRun));
    handle.PendingOncePerRun.Enqueue(Entry("o3", "C", SelfRescheduleOption.OncePerRun));
    handle.AddTimerFiring(Entry("t1", "B", SelfRescheduleOption.Timer, DateTimeOffset.UtcNow.AddMinutes(5)));

    handle.RemovePendingBookings("A").Should().Be(1);

    handle.PendingOncePerRun.Select(e => e.Id).Should().Equal("o1", "o3");
    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.SequenceId.Should().Be("B");
  }

  [Fact]
  public void KeepsTemplateHoldEntries() {
    var handle = NewHandle();
    handle.PendingNextCycleStart.Enqueue(Entry("at-queue-start:0", "A", SelfRescheduleOption.AtQueueStart));
    handle.PendingNextCycleStart.Enqueue(Entry("n1", "A", SelfRescheduleOption.AtQueueStart));

    handle.RemovePendingBookings("A").Should().Be(1);

    handle.PendingNextCycleStart.Should().ContainSingle().Which.Id.Should().Be("at-queue-start:0");
  }

  [Fact]
  public void KeepsEveryStepInjectionsAndLiveSchedules() {
    var handle = NewHandle();
    handle.EveryStepInjections["A"] = Entry("e1", "A", SelfRescheduleOption.EveryStep);
    handle.PendingLiveSchedules["A"] = DateTimeOffset.UtcNow.AddMinutes(1);

    handle.RemovePendingBookings("A").Should().Be(0);

    handle.EveryStepInjections.Should().ContainKey("A");
    handle.PendingLiveSchedules.Should().ContainKey("A");
  }

  [Fact]
  public void ReturnsZeroWhenNothingIsPending() {
    NewHandle().RemovePendingBookings("A").Should().Be(0);
  }
}
