using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 105: the file store of the run statistics. Each test uses a new temp folder.
/// </summary>
public sealed class FileSequenceRunStatisticsStoreTests : IDisposable {
  private static readonly DateTimeOffset T0 = new(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(2));
  private readonly string _root;

  public FileSequenceRunStatisticsStoreTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotRunStatsTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
  }

  private string FileFor(string queueId) => Path.Combine(_root, FileSequenceRunStatisticsStore.FolderName, queueId + ".json");

  private static SequenceRunRecord Record(SequenceRunStatus status, int minute = 0) =>
    new() { StartedAt = T0.AddMinutes(minute), EndedAt = T0.AddMinutes(minute).AddSeconds(10), Status = status };

  [Fact]
  public async Task RecordThenReadGivesTheEntry() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await store.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Success));
    await store.RecordAsync("q1", "seq-b", Record(SequenceRunStatus.Failure));

    var one = await store.GetAsync("q1", "seq-a");
    one!.SuccessCount.Should().Be(1);
    one.LastRunStatus.Should().Be(SequenceRunStatus.Success);

    var all = await store.GetForQueueAsync("q1");
    all.Keys.Should().BeEquivalentTo("seq-a", "seq-b");
    all["seq-b"].FailureCount.Should().Be(1);
    (await store.GetAsync("q1", "unknown")).Should().BeNull();
  }

  [Fact]
  public async Task ANewStoreOnTheSameFolderReadsTheSameValues() {
    using (var first = new FileSequenceRunStatisticsStore(_root)) {
      await first.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Success));
      await first.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Cancelled, 5));
    }

    using var second = new FileSequenceRunStatisticsStore(_root);
    var stats = await second.GetAsync("q1", "seq-a");

    stats!.SuccessCount.Should().Be(1);
    stats.CancelledCount.Should().Be(1);
    stats.LastRunStatus.Should().Be(SequenceRunStatus.Cancelled);
    stats.LastSuccessAt.Should().Be(T0.AddSeconds(10));
    stats.LastRunStartedAt!.Value.Offset.Should().Be(TimeSpan.FromHours(2));
    stats.RecentRuns.Should().HaveCount(2);
  }

  [Fact]
  public async Task AnAbsentFileGivesAnEmptyMap() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    (await store.GetForQueueAsync("q-none")).Should().BeEmpty();
    File.Exists(FileFor("q-none")).Should().BeFalse();
  }

  [Theory]
  [InlineData("{ not json")]
  [InlineData("{\"schemaVersion\": 2, \"queueId\": \"q1\", \"sequences\": {}}")]
  public async Task ADamagedFileGivesAnEmptyMapAndOneWarning(string content) {
    Directory.CreateDirectory(Path.GetDirectoryName(FileFor("q1"))!);
    await File.WriteAllTextAsync(FileFor("q1"), content);
    var logger = new RunStatisticsTestLogger<FileSequenceRunStatisticsStore>();
    using var store = new FileSequenceRunStatisticsStore(_root, logger);

    (await store.GetForQueueAsync("q1")).Should().BeEmpty();
    (await store.GetAsync("q1", "seq-a")).Should().BeNull();

    logger.WarningCount.Should().Be(1);
  }

  [Fact]
  public async Task TheNextRecordHealsADamagedFile() {
    Directory.CreateDirectory(Path.GetDirectoryName(FileFor("q1"))!);
    await File.WriteAllTextAsync(FileFor("q1"), "{ not json");
    using (var store = new FileSequenceRunStatisticsStore(_root)) {
      await store.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Success));
    }

    using var reread = new FileSequenceRunStatisticsStore(_root);
    (await reread.GetAsync("q1", "seq-a"))!.SuccessCount.Should().Be(1);
  }

  [Fact]
  public async Task DeleteRemovesTheFileAndTheCache() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await store.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Success));
    File.Exists(FileFor("q1")).Should().BeTrue();

    await store.DeleteQueueAsync("q1");

    File.Exists(FileFor("q1")).Should().BeFalse();
    (await store.GetForQueueAsync("q1")).Should().BeEmpty();
  }

  [Fact]
  public async Task DeleteWithNoFileGivesNoError() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    var act = () => store.DeleteQueueAsync("q-never");
    await act.Should().NotThrowAsync();
  }

  [Fact]
  public async Task ARecordAfterTheDeleteOfTheQueueMakesNoOrphanFile() {
    // Analyze finding I1: a run that ends after the queue delete must not make the file again.
    using var store = new FileSequenceRunStatisticsStore(_root);
    await store.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Success));
    await store.DeleteQueueAsync("q1");

    await store.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Success, 5));

    File.Exists(FileFor("q1")).Should().BeFalse();
    (await store.GetForQueueAsync("q1")).Should().BeEmpty();
  }

  [Fact]
  public async Task AReturnedCopyDoesNotChangeTheCache() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await store.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Success));

    var copy = await store.GetAsync("q1", "seq-a");
    copy!.Apply(Record(SequenceRunStatus.Failure, 1));
    copy.RecentRuns.Clear();
    var map = await store.GetForQueueAsync("q1");
    map["seq-a"].SuccessCount = 99;

    var again = await store.GetAsync("q1", "seq-a");
    again!.SuccessCount.Should().Be(1);
    again.FailureCount.Should().Be(0);
    again.RecentRuns.Should().ContainSingle();
  }

  [Theory]
  [InlineData("..")]
  [InlineData("a/b")]
  [InlineData("a\\b")]
  [InlineData("..\\x")]
  [InlineData("")]
  public async Task AnUnsafeQueueIdThrows(string queueId) {
    using var store = new FileSequenceRunStatisticsStore(_root);
    var act = () => store.RecordAsync(queueId, "seq-a", Record(SequenceRunStatus.Success));
    await act.Should().ThrowAsync<ArgumentException>();
    var read = () => store.GetForQueueAsync(queueId);
    await read.Should().ThrowAsync<ArgumentException>();
  }

  [Fact]
  public async Task TheFileUsesLowerCaseStatusText() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await store.RecordAsync("q1", "seq-a", Record(SequenceRunStatus.Cancelled));

    var text = await File.ReadAllTextAsync(FileFor("q1"));

    text.Should().Contain("\"lastRunStatus\": \"cancelled\"");
    text.Should().Contain("\"status\": \"cancelled\"");
    text.Should().Contain("\"schemaVersion\": 1");
    text.Should().NotContain("Cancelled");
  }
}
