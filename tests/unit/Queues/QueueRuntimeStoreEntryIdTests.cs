using System.Linq;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 171: <see cref="IQueueRuntimeStore.ReplaceEntries"/> swaps the entries of a queue in one step and
/// keeps the entry IDs that the caller gives (the IDs of the template entries).
/// </summary>
public sealed class QueueRuntimeStoreEntryIdTests {
  [Fact]
  public void ReplaceEntriesKeepsTheGivenIdsAndOrder() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "old");

    var result = store.ReplaceEntries("q1", new[] {
      new QueueEntry { EntryId = "idB", SequenceId = "seq-b" },
      new QueueEntry { EntryId = "idA", SequenceId = "seq-a" },
    });

    result.Select(e => e.EntryId).Should().Equal("idB", "idA");
    store.GetEntries("q1").Select(e => e.SequenceId).Should().Equal("seq-b", "seq-a");
  }

  [Fact]
  public void ReplaceEntriesCreatesAnIdForAnEntryWithNone() {
    var store = new QueueRuntimeStore();

    var result = store.ReplaceEntries("q1", new[] {
      new QueueEntry { EntryId = string.Empty, SequenceId = "seq-a" },
      new QueueEntry { EntryId = string.Empty, SequenceId = "seq-a" },
    });

    result.Select(e => e.EntryId).Should().OnlyHaveUniqueItems();
    result.Should().OnlyContain(e => !string.IsNullOrEmpty(e.EntryId));
  }

  [Fact]
  public void ReplaceEntriesSwapsAllEntriesInOneStep() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "old-a");
    store.AddEntry("q1", "old-b");

    store.ReplaceEntries("q1", new[] { new QueueEntry { EntryId = "idA", SequenceId = "seq-a" } });

    store.GetEntries("q1").Should().ContainSingle().Which.EntryId.Should().Be("idA");
  }

  [Fact]
  public void ReplaceEntriesWithEmptyInputClearsEntriesAndMarksState() {
    var store = new QueueRuntimeStore();
    store.AddEntry("q1", "old-a");

    var result = store.ReplaceEntries("q1", System.Array.Empty<QueueEntry>());

    result.Should().BeEmpty();
    store.HasRuntimeState("q1").Should().BeTrue();
  }

  [Fact]
  public void ReplaceEntriesCopiesTheInputSoALaterChangeOfTheCallerListDoesNotLeakIn() {
    var store = new QueueRuntimeStore();
    var source = new QueueEntry { EntryId = "idA", SequenceId = "seq-a" };

    store.ReplaceEntries("q1", new[] { source });
    source.SequenceId = "changed";

    store.GetEntries("q1").Single().SequenceId.Should().Be("seq-a");
  }
}
