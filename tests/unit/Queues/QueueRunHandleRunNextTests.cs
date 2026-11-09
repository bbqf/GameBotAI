using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 184: the run-next register of <see cref="QueueRunHandle"/>.</summary>
public sealed class QueueRunHandleRunNextTests {
  private static readonly DateTimeOffset T1 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

  private static QueueRunHandle NewHandle() => new() { QueueId = "q1", Cts = new CancellationTokenSource() };

  private static PendingRunNext Request(string entryId, string sequenceId = "seq") =>
    new(entryId, sequenceId, ParameterScope.Empty, T1);

  [Fact]
  public void AddingARequestWorks() {
    var handle = NewHandle();

    handle.TryAddRunNext(Request("e1")).Should().BeTrue();

    handle.HasPendingRunNext.Should().BeTrue();
    handle.IsRunNextQueued("e1").Should().BeTrue();
    handle.SnapshotRunNext().Should().ContainSingle().Which.EntryId.Should().Be("e1");
  }

  [Fact]
  public void ASecondAddForTheSameEntryAddsNothing() {
    var handle = NewHandle();
    handle.TryAddRunNext(Request("e1"));

    handle.TryAddRunNext(Request("e1", "other")).Should().BeFalse();

    handle.SnapshotRunNext().Should().ContainSingle().Which.SequenceId.Should().Be("seq");
  }

  [Fact]
  public void RequestsKeepTheOrderOfAdding() {
    var handle = NewHandle();
    handle.TryAddRunNext(Request("e1"));
    handle.TryAddRunNext(Request("e2"));
    handle.TryAddRunNext(Request("e3"));

    handle.SnapshotRunNext().Select(r => r.EntryId).Should().Equal("e1", "e2", "e3");
  }

  [Fact]
  public void TakingReturnsTheRequestsInOrderAndEmptiesTheRegister() {
    var handle = NewHandle();
    handle.TryAddRunNext(Request("e1"));
    handle.TryAddRunNext(Request("e2"));

    handle.TryTakeRunNext(out var first).Should().BeTrue();
    handle.TryTakeRunNext(out var second).Should().BeTrue();
    handle.TryTakeRunNext(out _).Should().BeFalse();

    first.EntryId.Should().Be("e1");
    second.EntryId.Should().Be("e2");
    handle.HasPendingRunNext.Should().BeFalse();
  }

  [Fact]
  public void APutBackRequestKeepsItsPlaceAtTheFront() {
    var handle = NewHandle();
    handle.TryAddRunNext(Request("e1"));
    handle.TryAddRunNext(Request("e2"));
    handle.TryTakeRunNext(out var held);

    handle.PutBackRunNext(held);

    handle.SnapshotRunNext().Select(r => r.EntryId).Should().Equal("e1", "e2");
  }

  [Fact]
  public void APutBackDoesNotDuplicateAnEntryThatWaitsAgain() {
    var handle = NewHandle();
    handle.TryAddRunNext(Request("e1"));
    handle.TryTakeRunNext(out var held);
    handle.TryAddRunNext(Request("e1")).Should().BeTrue("the register no longer holds the entry");

    handle.PutBackRunNext(held);

    handle.SnapshotRunNext().Should().ContainSingle();
  }

  [Fact]
  public void APendingRequestCountsAsSelfRescheduleWork() {
    var handle = NewHandle();
    handle.HasPendingSelfRescheduleWork.Should().BeFalse();

    handle.TryAddRunNext(Request("e1"));

    handle.HasPendingSelfRescheduleWork.Should().BeTrue();
  }

  [Fact]
  public void ClearingEmptiesTheRegister() {
    var handle = NewHandle();
    handle.TryAddRunNext(Request("e1"));

    handle.ClearRunNext();

    handle.HasPendingRunNext.Should().BeFalse();
    handle.SnapshotRunNext().Should().BeEmpty();
  }

  [Fact]
  public void TwoAddsAtTheSameTimeLeaveOneRequest() {
    var handle = NewHandle();
    var accepted = 0;

    System.Threading.Tasks.Parallel.For(0, 32, _ => {
      if (handle.TryAddRunNext(Request("e1"))) Interlocked.Increment(ref accepted);
    });

    accepted.Should().Be(1);
    handle.SnapshotRunNext().Should().ContainSingle();
  }
}
