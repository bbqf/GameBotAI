using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

public sealed class QueueRuntimeStoreHasStateTests {
  [Fact]
  public void HasRuntimeStateIsFalseBeforeAnyOperation() {
    var store = new QueueRuntimeStore();
    store.HasRuntimeState("q1").Should().BeFalse();
  }

  [Fact]
  public void GetEntriesDoesNotMaterializeState() {
    var store = new QueueRuntimeStore();
    _ = store.GetEntries("q1");
    store.HasRuntimeState("q1").Should().BeFalse();
  }

  [Fact]
  public void HasRuntimeStateIsTrueAfterAddEntry() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "seq-a");
    store.HasRuntimeState("q1").Should().BeTrue();
  }

  [Fact]
  public void HasRuntimeStateIsTrueAfterSetEntries() {
    var store = new QueueRuntimeStore();
    var ids = new[] { "seq-a" };
    store.SetEntries("q1", ids);
    store.HasRuntimeState("q1").Should().BeTrue();
  }

  [Fact]
  public void HasRuntimeStateIsTrueAfterSetStatus() {
    var store = new QueueRuntimeStore();
    store.SetStatus("q1", QueueExecutionStatus.Running);
    store.HasRuntimeState("q1").Should().BeTrue();
  }

  [Fact]
  public void HasRuntimeStateIsFalseAgainAfterRemove() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "seq-a");
    store.Remove("q1");
    store.HasRuntimeState("q1").Should().BeFalse();
  }

  [Fact]
  public void HasRuntimeStateRemainsTrueWhenEntriesClearedButStateExists() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "seq-a");
    store.SetEntries("q1", System.Array.Empty<string>());
    store.GetEntries("q1").Should().BeEmpty();
    store.HasRuntimeState("q1").Should().BeTrue();
  }
}
