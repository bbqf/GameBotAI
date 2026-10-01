using System;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 123 (FR-005, FR-015): a Cancel stops a OncePerRun booking that is in the drain copy.</summary>
public sealed class QueueRunHandleDrainCancelTests {
  private static QueueRunHandle NewHandle() => new() { QueueId = "q1", Cts = new CancellationTokenSource() };

  private static SelfRescheduleEntry Entry(string id, string sequenceId) =>
    new(id, sequenceId, SelfRescheduleOption.OncePerRun, null);

  [Fact]
  public void CancelMarksInFlightEntriesOfTheSequence() {
    var handle = NewHandle();
    handle.BeginOncePerRunDrain(new[] { Entry("b1", "A"), Entry("b2", "A"), Entry("x1", "B") });

    handle.RemovePendingBookings("A").Should().Be(2);

    handle.TryConsumeCancelled("b1").Should().BeTrue();
    handle.TryConsumeCancelled("b2").Should().BeTrue();
    handle.TryConsumeCancelled("x1").Should().BeFalse();
  }

  [Fact]
  public void FiringEntryIsNotCountedByItsOwnCancel() {
    var handle = NewHandle();
    handle.BeginOncePerRunDrain(new[] { Entry("b1", "A"), Entry("b2", "A") });

    // The loop consumes b1 before it fires it. The Cancel then runs inside that firing.
    handle.TryConsumeCancelled("b1").Should().BeFalse();
    handle.RemovePendingBookings("A").Should().Be(1);

    handle.TryConsumeCancelled("b2").Should().BeTrue();
  }

  [Fact]
  public void BookingAddedAfterCancelIsNotMarked() {
    var handle = NewHandle();
    handle.BeginOncePerRunDrain(new[] { Entry("b1", "A") });
    handle.RemovePendingBookings("A");

    handle.PendingOncePerRun.Enqueue(Entry("b-new", "A"));

    handle.TryConsumeCancelled("b-new").Should().BeFalse();
    handle.PendingOncePerRun.Should().ContainSingle();
  }

  [Fact]
  public void EndDrainClearsTheMarks() {
    var handle = NewHandle();
    handle.BeginOncePerRunDrain(new[] { Entry("b1", "A") });
    handle.RemovePendingBookings("A");

    handle.EndOncePerRunDrain();

    handle.TryConsumeCancelled("b1").Should().BeFalse();
    handle.RemovePendingBookings("A").Should().Be(0);
  }
}
