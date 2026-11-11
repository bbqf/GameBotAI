using FluentAssertions;
using GameBot.Service.Security;
using GameBot.UnitTests.Queues;
using Xunit;

namespace GameBot.UnitTests.Security;

internal sealed class Sample {
  public int Count { get; set; }

  public string? Text { get; set; }
}

public sealed class AuthFileStoreTests : IDisposable {
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "gamebot-authstore-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

  public void Dispose() {
    if (Directory.Exists(_dir)) {
      Directory.Delete(_dir, recursive: true);
    }
  }

  private AuthFileStore<Sample> NewStore(Func<Sample, bool>? isEmpty = null) =>
    new(Path.Combine(_dir, "sample.json"), _clock, isEmpty);

  [Fact]
  public void ReadOfAbsentFileGivesEmptyValue() {
    var store = NewStore();

    store.Snapshot().Count.Should().Be(0);
    File.Exists(Path.Combine(_dir, "sample.json")).Should().BeFalse();
  }

  [Fact]
  public void UpdateSavesAndReadsBack() {
    var store = NewStore();

    var result = store.Update(v => {
      v.Count = 3;
      v.Text = "abc";
      return v.Count * 2;
    });

    result.Should().Be(6);
    store.Snapshot().Text.Should().Be("abc");
    NewStore().Snapshot().Count.Should().Be(3);
    Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
  }

  [Fact]
  public void SecondUpdateReplacesFile() {
    var store = NewStore();
    store.Update(v => v.Count = 1);
    store.Update(v => v.Count += 1);

    NewStore().Snapshot().Count.Should().Be(2);
  }

  [Fact]
  public void SecondInstanceReloadsAfterChange() {
    var first = NewStore();
    var second = NewStore();
    first.Update(v => v.Count = 1);
    second.Snapshot().Count.Should().Be(1);
    var version = second.Version;

    first.Update(v => v.Count = 7);
    _clock.Advance(TimeSpan.FromSeconds(2));
    // The write time can be equal on a fast file system. Make it different.
    File.SetLastWriteTimeUtc(Path.Combine(_dir, "sample.json"), DateTime.UtcNow.AddMinutes(1));

    second.Snapshot().Count.Should().Be(7);
    second.Version.Should().BeGreaterThan(version);
  }

  [Fact]
  public void FileIsDeletedWhenValueIsEmpty() {
    var store = NewStore(v => v.Count == 0);
    store.Update(v => v.Count = 1);
    File.Exists(Path.Combine(_dir, "sample.json")).Should().BeTrue();

    store.Update(v => v.Count = 0);

    File.Exists(Path.Combine(_dir, "sample.json")).Should().BeFalse();
  }

  [Fact]
  public void DamagedFileGivesEmptyValue() {
    Directory.CreateDirectory(_dir);
    File.WriteAllText(Path.Combine(_dir, "sample.json"), "{ not json");

    NewStore().Snapshot().Count.Should().Be(0);
  }

  [Fact]
  public async Task LockIsRetriedUntilFree() {
    var store = NewStore();
    store.Update(v => v.Count = 1);
    var lockPath = Path.Combine(_dir, "sample.json.lock");
    var holder = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    await using var holderScope = holder.ConfigureAwait(true);
    var release = Task.Run(async () => {
      await Task.Delay(150).ConfigureAwait(false);
      await holder.DisposeAsync().ConfigureAwait(false);
    });

    await Task.Run(() => store.Update(v => v.Count = 2)).ConfigureAwait(true);
    await release.ConfigureAwait(true);

    NewStore().Snapshot().Count.Should().Be(2);
  }
}

