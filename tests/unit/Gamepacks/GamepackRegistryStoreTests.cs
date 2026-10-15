using System;
using System.IO;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 143: the registry store and the owner resolver.</summary>
public sealed class GamepackRegistryStoreTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotGamepackStore", Guid.NewGuid().ToString("N"));

  public GamepackRegistryStoreTests() => Directory.CreateDirectory(_root);

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  [Fact]
  public void MissingFileGivesAnEmptyRegistry() {
    var store = new GamepackRegistryStore(_root);
    var registry = store.Load();
    registry.Gamepacks.Should().BeEmpty();
    registry.Owners.Should().BeEmpty();
    store.RegistryExists.Should().BeFalse();
  }

  [Fact]
  public void RegistryRoundTrips() {
    var store = new GamepackRegistryStore(_root);
    var registry = new GamepackRegistry();
    registry.Gamepacks["pack"] = new InstalledGamepack {
      Name = "Pack",
      Version = "1",
      Changelog = "c",
      RequiredApiMajor = 2,
      Objects = { new GamepackObjectRef { Type = "sequence", Name = "S", Id = "s1" } },
      Previous = new PreviousVersion { Version = "0" }
    };
    registry.Owners[GamepackRegistry.ObjectKey("sequence", "S")] = new OwnerEntry { Gamepack = "Pack", Version = "1", Id = "s1" };

    store.Save(registry);
    var loaded = store.Load();

    loaded.Gamepacks["pack"].Name.Should().Be("Pack");
    loaded.Gamepacks["pack"].RequiredApiMajor.Should().Be(2);
    loaded.Gamepacks["pack"].Previous!.Version.Should().Be("0");
    loaded.Gamepacks["pack"].Objects.Should().ContainSingle(o => o.Id == "s1");
    loaded.Owners["sequence:s"].Gamepack.Should().Be("Pack");
  }

  [Fact]
  public void AtomicReplaceLeavesNoTemporaryFile() {
    var store = new GamepackRegistryStore(_root);
    store.Save(new GamepackRegistry());
    store.Save(new GamepackRegistry());
    Directory.GetFiles(Path.Combine(_root, "gamepacks"), "*.tmp").Should().BeEmpty();
    store.RegistryExists.Should().BeTrue();
    store.DeleteRegistry();
    store.RegistryExists.Should().BeFalse();
  }

  [Fact]
  public void UpdateReadsChangesAndWrites() {
    var store = new GamepackRegistryStore(_root);
    store.Update(r => r.Owners["image:x"] = new OwnerEntry { Gamepack = "P", Version = "1", Id = "x" });
    store.Load().Owners.Should().ContainKey("image:x");
  }

  [Fact]
  public void PreviousFileRoundTripsAndDeletes() {
    var store = new GamepackRegistryStore(_root);
    store.ReadPrevious("Pack").Should().BeNull();
    store.WritePrevious("Pack", "{\"a\":1}");
    store.ReadPrevious("pack").Should().Be("{\"a\":1}");
    store.DeletePrevious("PACK");
    store.ReadPrevious("Pack").Should().BeNull();
    store.DeletePrevious("Pack");
  }

  [Fact]
  public void PreviousFileRefusesAnInvalidName() {
    var store = new GamepackRegistryStore(_root);
    var act = () => store.WritePrevious("..\\evil", "x");
    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void OwnerResolverGivesTheUserWhenThereIsNoEntry() {
    var resolver = new GamepackOwnerResolver(new GamepackRegistry());
    var owner = resolver.Resolve("sequence", "s1");
    owner.IsUser.Should().BeTrue();
    owner.ToText().Should().Be("user");
  }

  [Fact]
  public void OwnerResolverFindsAGamepackOwnerById() {
    var registry = new GamepackRegistry();
    registry.Owners[GamepackRegistry.ObjectKey("command", "Tap")] = new OwnerEntry { Gamepack = "Pack", Version = "1.2", Id = "c1" };
    var resolver = new GamepackOwnerResolver(registry);

    var owner = resolver.Resolve("command", "C1");
    owner.IsUser.Should().BeFalse();
    owner.Name.Should().Be("Pack");
    owner.ToText().Should().Be("Pack 1.2");
    resolver.Resolve("sequence", "c1").IsUser.Should().BeTrue("the type is part of the key");
    resolver.Resolve("command", "other").IsUser.Should().BeTrue("an owner entry gives an owner only to its own object id");
  }
}
