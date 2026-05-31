using System.Linq;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

public sealed class QueueRuntimeStoreEntriesTests {
  [Fact]
  public void AddEntryAppendsInInsertionOrder() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "seq-a");
    store.AddEntry("q1", "seq-b");
    store.AddEntry("q1", "seq-c");

    store.GetEntries("q1").Select(e => e.SequenceId)
      .Should().ContainInOrder("seq-a", "seq-b", "seq-c");
  }

  [Fact]
  public void RemoveEntryKeepsRelativeOrder() {
    var store = new QueueRuntimeStore();
    var a = store.AddEntry("q1", "seq-a");
    var b = store.AddEntry("q1", "seq-b");
    var c = store.AddEntry("q1", "seq-c");

    store.RemoveEntry("q1", b.EntryId).Should().BeTrue();
    store.GetEntries("q1").Select(e => e.EntryId)
      .Should().ContainInOrder(a.EntryId, c.EntryId);
  }

  [Fact]
  public void DuplicateSequenceIdAllowed() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "seq-a");
    store.AddEntry("q1", "seq-a");

    store.GetEntries("q1").Should().HaveCount(2);
  }

  [Fact]
  public void UnknownQueueReturnsEmptyEntries() {
    var store = new QueueRuntimeStore();
    store.GetEntries("missing").Should().BeEmpty();
  }

  [Fact]
  public void RemoveUnknownEntryReturnsFalse() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "seq-a");
    store.RemoveEntry("q1", "nope").Should().BeFalse();
  }

  [Fact]
  public void RemoveDiscardsAllRuntimeState() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "seq-a");
    store.SetStatus("q1", QueueExecutionStatus.Running);

    store.Remove("q1");

    store.GetEntries("q1").Should().BeEmpty();
    store.GetStatus("q1").Should().Be(QueueExecutionStatus.Stopped);
  }
}
