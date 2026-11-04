using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 178 (T028): the four reinstall members of the registry store.</summary>
public sealed class GamepackReinstallStoreTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotReinstallStore", Guid.NewGuid().ToString("N"));
  private readonly GamepackRegistryStore _store;

  public GamepackReinstallStoreTests() {
    Directory.CreateDirectory(_root);
    _store = new GamepackRegistryStore(_root);
  }

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  [Fact]
  public void AMissingFileGivesNull() {
    _store.ReadReinstall("pns").Should().BeNull();
  }

  [Fact]
  public void AWriteThenAReadGivesTheSameText() {
    _store.WriteReinstall("pns", "{\"a\":1}");

    _store.ReadReinstall("pns").Should().Be("{\"a\":1}");
    File.Exists(Path.Combine(_root, "gamepacks", "reinstall", "pns.json")).Should().BeTrue();
  }

  [Fact]
  public void ASecondWriteReplacesTheFileAndLeavesNoTemporaryFile() {
    _store.WriteReinstall("pns", "one");
    _store.WriteReinstall("pns", "two");

    _store.ReadReinstall("pns").Should().Be("two");
    Directory.GetFiles(Path.Combine(_root, "gamepacks", "reinstall"), "*.tmp").Should().BeEmpty();
  }

  [Fact]
  public void ADeleteRemovesTheFileAndAMissingFileIsNotAnError() {
    _store.WriteReinstall("pns", "one");

    _store.DeleteReinstall("pns");
    var again = () => _store.DeleteReinstall("pns");

    _store.ReadReinstall("pns").Should().BeNull();
    again.Should().NotThrow();
  }

  [Fact]
  public void ThePathIsTheFullPathOfTheFileAndNullForABadName() {
    _store.ReinstallFilePath("PNS").Should().Be(Path.GetFullPath(Path.Combine(_root, "gamepacks", "reinstall", "pns.json")));
    _store.ReinstallFilePath("..\\bad").Should().BeNull();
  }

  [Fact]
  public void ABadNameIsRefusedByTheFileMembers() {
    var read = () => _store.ReadReinstall("..\\bad");

    read.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void AnOldRegistryFileWithoutTheReinstallMemberStillLoads() {
    var folder = Path.Combine(_root, "gamepacks");
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "registry.json"),
      "{\"gamepacks\":{\"pns\":{\"name\":\"pns\",\"version\":\"1\",\"changelog\":\"\",\"installedAtUtc\":\"2026-01-01T00:00:00Z\",\"objects\":[]}},\"owners\":{}}");

    var registry = _store.Load();

    registry.Gamepacks["pns"].Reinstall.Should().BeNull();
    registry.Gamepacks["pns"].Version.Should().Be("1");
  }

  [Fact]
  public void TheReinstallMemberRoundTrips() {
    var registry = new GamepackRegistry();
    registry.Gamepacks["pns"] = new InstalledGamepack { Name = "pns", Version = "1", Reinstall = new PreviousVersion { Version = "1" } };
    _store.Save(registry);

    var loaded = _store.Load();

    loaded.Gamepacks["pns"].Reinstall.Should().NotBeNull();
    loaded.Gamepacks["pns"].Reinstall!.Version.Should().Be("1");
    JsonDocument.Parse(File.ReadAllText(_store.RegistryPath)).RootElement.GetProperty("gamepacks").GetProperty("pns").TryGetProperty("reinstall", out _).Should().BeTrue();
  }

  [Fact]
  public void AStoreWithoutFilesUsesTheDefaultMembers() {
    IGamepackRegistryStore store = new NoFileStore();

    store.ReadReinstall("pns").Should().BeNull();
    store.ReinstallFilePath("pns").Should().BeNull();
    store.Invoking(s => { s.WriteReinstall("pns", "x"); s.DeleteReinstall("pns"); }).Should().NotThrow();
  }

  private sealed class NoFileStore : IGamepackRegistryStore {
    public GamepackRegistry Load() => new();
    public bool RegistryExists => false;
    public void DeleteRegistry() { }
    public void Save(GamepackRegistry registry) { }
    public void Update(Action<GamepackRegistry> change) { }
    public string? ReadPrevious(string name) => null;
    public void WritePrevious(string name, string text) { }
    public void DeletePrevious(string name) { }
  }
}
