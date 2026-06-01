using System.Linq;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

public sealed class QueueRuntimeStoreSetEntriesTests {
  [Fact]
  public void SetEntriesReplacesExistingPreservingOrder() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "old-a");
    store.AddEntry("q1", "old-b");
    var ids = new[] { "seq-a", "seq-b", "seq-a" };

    var result = store.SetEntries("q1", ids);

    result.Select(e => e.SequenceId).Should().ContainInOrder("seq-a", "seq-b", "seq-a");
    store.GetEntries("q1").Select(e => e.SequenceId).Should().ContainInOrder("seq-a", "seq-b", "seq-a");
  }

  [Fact]
  public void SetEntriesAssignsFreshUniqueEntryIds() {
    var store = new QueueRuntimeStore();
    var input = new[] { "seq-a", "seq-a" };

    var result = store.SetEntries("q1", input);

    var ids = result.Select(e => e.EntryId).ToList();
    ids.Should().OnlyHaveUniqueItems();
    ids.Should().AllSatisfy(id => id.Should().NotBeNullOrWhiteSpace());
  }

  [Fact]
  public void SetEntriesWithEmptyInputClearsEntries() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "old-a");

    var result = store.SetEntries("q1", System.Array.Empty<string>());

    result.Should().BeEmpty();
    store.GetEntries("q1").Should().BeEmpty();
  }
}
