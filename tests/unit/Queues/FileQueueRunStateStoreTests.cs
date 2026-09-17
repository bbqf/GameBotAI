using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 098 (#203): the durable record of which queues have a live run, read once at service start
/// to resume opted-in queues.
/// </summary>
public sealed class FileQueueRunStateStoreTests : IDisposable {
  private readonly string _root;

  public FileQueueRunStateStoreTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotRunStateTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
  }

  private string FilePath => Path.Combine(_root, "queue-run-state.json");

  [Fact]
  public async Task MissingFileListsEmpty() {
    using var store = new FileQueueRunStateStore(_root);
    (await store.ListRunningAsync()).Should().BeEmpty();
  }

  [Fact]
  public async Task MarkAddsIdOnceEvenWhenRepeated() {
    using var store = new FileQueueRunStateStore(_root);
    await store.MarkRunningAsync("q1");
    await store.MarkRunningAsync("q1");

    (await store.ListRunningAsync()).Should().Equal("q1");
  }

  [Fact]
  public async Task ClearRemovesIdAndIgnoresUnknownIds() {
    using var store = new FileQueueRunStateStore(_root);
    await store.MarkRunningAsync("q1");
    await store.MarkRunningAsync("q2");

    await store.ClearAsync("q1");
    await store.ClearAsync("never-marked");

    (await store.ListRunningAsync()).Should().Equal("q2");
  }

  [Fact]
  public async Task ClearOfUnknownIdDoesNotCreateTheFile() {
    using var store = new FileQueueRunStateStore(_root);
    await store.ClearAsync("q1");
    File.Exists(FilePath).Should().BeFalse();
  }

  [Fact]
  public async Task IdsSurviveANewStoreInstance() {
    using (var first = new FileQueueRunStateStore(_root)) {
      await first.MarkRunningAsync("q1");
    }

    using var second = new FileQueueRunStateStore(_root);
    (await second.ListRunningAsync()).Should().Equal("q1");
  }

  [Fact]
  public async Task EmptyFileListsEmpty() {
    await File.WriteAllTextAsync(FilePath, string.Empty);
    using var store = new FileQueueRunStateStore(_root);
    (await store.ListRunningAsync()).Should().BeEmpty();
  }

  [Fact]
  public async Task CorruptFileMakesListThrowButWritesHealIt() {
    await File.WriteAllTextAsync(FilePath, "{ not json");
    using var store = new FileQueueRunStateStore(_root);

    var list = async () => await store.ListRunningAsync();
    await list.Should().ThrowAsync<InvalidDataException>();

    await store.MarkRunningAsync("q1");
    (await store.ListRunningAsync()).Should().Equal("q1");
  }

  [Fact]
  public async Task ConcurrentMarksAllSurvive() {
    using var store = new FileQueueRunStateStore(_root);
    var ids = Enumerable.Range(0, 20).Select(i => $"q{i}").ToList();

    await Task.WhenAll(ids.Select(id => Task.Run(() => store.MarkRunningAsync(id))));

    (await store.ListRunningAsync()).Should().BeEquivalentTo(ids);
  }
}
