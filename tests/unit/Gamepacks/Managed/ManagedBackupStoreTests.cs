using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.QueueSettings;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861, CA1305, CA1307

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T020): the backup store.</summary>
public sealed class ManagedBackupStoreTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotBackup", Guid.NewGuid().ToString("N"));

  public ManagedBackupStoreTests() => Directory.CreateDirectory(_root);

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  private sealed class StepTime : TimeProvider {
    private DateTimeOffset _now = new(2026, 10, 16, 10, 15, 0, 123, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() {
      _now = _now.AddMilliseconds(1);
      return _now;
    }
  }

  [Fact]
  public async Task AWriteGivesAnIdWithTheTimeStampAndTheNameAndTheFileCanBeRead() {
    var store = new ManagedBackupStore(_root, new StepTime());

    var id = await store.WriteAsync("PNS", new SettingsDocument());

    id.Should().Be("20261016T101500124-pns");
    store.IsValidId(id).Should().BeTrue();
    store.Read(id).Should().Contain("formatVersion");
    File.Exists(Path.Combine(_root, "gamepacks", "backups", id + ".json")).Should().BeTrue();
  }

  [Theory]
  [InlineData("..\\..\\secret")]
  [InlineData("../x")]
  [InlineData("20261016T101500124-pns/../../x")]
  [InlineData("")]
  [InlineData("20261016T101500124-")]
  public void AnIdWithAWrongFormIsNotValidAndIsNeverRead(string id) {
    var store = new ManagedBackupStore(_root);

    store.IsValidId(id).Should().BeFalse();
    store.Read(id).Should().BeNull();
  }

  [Fact]
  public void AMissingFileReadsAsNull() {
    new ManagedBackupStore(_root).Read("20261016T101500124-pns").Should().BeNull();
  }

  [Fact]
  public async Task AWriteWithAnInvalidGamepackNameFails() {
    var store = new ManagedBackupStore(_root);

    Func<Task> act = () => store.WriteAsync("..\\bad", new SettingsDocument());

    await act.Should().ThrowAsync<ArgumentException>();
  }

  [Fact]
  public async Task OnlyTheNewestTwentyFilesStay() {
    var store = new ManagedBackupStore(_root, new StepTime());
    var ids = new System.Collections.Generic.List<string>();
    for (var i = 0; i < 25; i++) {
      ids.Add(await store.WriteAsync("pns", new SettingsDocument()));
    }

    var files = Directory.GetFiles(Path.Combine(_root, "gamepacks", "backups"), "*.json");
    files.Should().HaveCount(ManagedBackupStore.Retention);
    store.Read(ids[0]).Should().BeNull();
    store.Read(ids[24]).Should().NotBeNull();
    Directory.GetFiles(Path.Combine(_root, "gamepacks", "backups"), "*.tmp").Should().BeEmpty();
    files.Select(Path.GetFileNameWithoutExtension).Should().Contain(ids.Skip(5));
  }
}
