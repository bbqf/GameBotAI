using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 149 (T006): the take-over backup store.</summary>
public sealed class TakeOverBackupStoreTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotTakeOverStore", Guid.NewGuid().ToString("N"));

  public TakeOverBackupStoreTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  private static TakeOverBackup Backup() {
    var backup = new TakeOverBackup { CreatedAtUtc = DateTimeOffset.UtcNow, Gamepack = "pack", Version = "1.0" };
    backup.Entries.Add(new TakeOverBackupEntry {
      Type = "image",
      Name = "icon",
      ObjectId = "icon",
      PostHash = "abc",
      Image = new TakeOverBackupImage { ContentType = "image/png", Filename = "icon.png", DataBase64 = "AQID" }
    });
    return backup;
  }

  [Theory]
  [InlineData("to-20261016T101500123-1a2b3c4d", true)]
  [InlineData("to-20261016T101500123-1A2B3C4D", false)]
  [InlineData("to-20261016T101500123-1a2b3c4", false)]
  [InlineData("20261016T101500123-1a2b3c4d", false)]
  [InlineData("../to-20261016T101500123-1a2b3c4d", false)]
  [InlineData("to-20261016T101500123-1a2b3c4d.json", false)]
  [InlineData("", false)]
  public void OnlyTheExactIdFormIsValid(string id, bool expected) {
    new TakeOverBackupStore(_root).IsValidId(id).Should().Be(expected);
  }

  [Fact]
  public void ANullIdIsNotValid() {
    new TakeOverBackupStore(_root).IsValidId(null).Should().BeFalse();
  }

  [Fact]
  public void WriteThenReadGivesTheSameBackup() {
    var store = new TakeOverBackupStore(_root);

    var id = store.Write(Backup());
    var read = store.Read(id);

    store.IsValidId(id).Should().BeTrue();
    read.Should().NotBeNull();
    read!.Id.Should().Be(id);
    read.Gamepack.Should().Be("pack");
    read.Entries.Should().ContainSingle().Which.Image!.DataBase64.Should().Be("AQID");
    File.Exists(Path.Combine(_root, "gamepacks", "takeover-backups", id + ".json")).Should().BeTrue();
  }

  [Fact]
  public void TwoWritesInTheSameMillisecondGiveTwoIds() {
    var store = new TakeOverBackupStore(_root, new FixedTime());

    var first = store.Write(Backup());
    var second = store.Write(Backup());

    first.Should().NotBe(second);
    Directory.GetFiles(Path.Combine(_root, "gamepacks", "takeover-backups"), "*.json").Should().HaveCount(2);
  }

  [Fact]
  public void WriteLeavesNoTemporaryFileAndNeverPrunes() {
    var store = new TakeOverBackupStore(_root);
    for (var i = 0; i < 25; i++) {
      store.Write(Backup());
    }

    var files = Directory.GetFiles(Path.Combine(_root, "gamepacks", "takeover-backups"));
    files.Should().HaveCount(25);
    files.Any(f => f.EndsWith(".tmp", StringComparison.Ordinal)).Should().BeFalse();
  }

  [Fact]
  public void ReadOfAMissingOrMalformedIdGivesNull() {
    var store = new TakeOverBackupStore(_root);

    store.Read("to-20261016T101500123-1a2b3c4d").Should().BeNull();
    store.Read("..\\..\\secret").Should().BeNull();
  }

  [Fact]
  public void DeleteRemovesTheFileAndIgnoresAMissingFile() {
    var store = new TakeOverBackupStore(_root);
    var id = store.Write(Backup());

    store.Delete(id);
    store.Delete(id);
    store.Delete("../x");

    store.Read(id).Should().BeNull();
  }

  private sealed class FixedTime : TimeProvider {
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 16, 10, 15, 0, TimeSpan.Zero);
  }
}
