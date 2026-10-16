using System;
using System.IO;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 147 (T011): the settings store.</summary>
public sealed class GamepackCatalogSettingsStoreTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotCatalogStore", Guid.NewGuid().ToString("N"));

  public GamepackCatalogSettingsStoreTests() => Directory.CreateDirectory(_root);

  public void Dispose() => Directory.Delete(_root, recursive: true);

  [Fact]
  public void AMissingFileGivesEmptySettings() {
    var settings = new GamepackCatalogSettingsStore(_root).Load();

    settings.Location.Should().BeNull();
    settings.Token.Should().BeNull();
    settings.AllowInsecure.Should().BeFalse();
  }

  [Fact]
  public void SettingsRoundTripAndSurviveANewStore() {
    new GamepackCatalogSettingsStore(_root).Save("https://example.com/c", true, "secret");

    var loaded = new GamepackCatalogSettingsStore(_root).Load();

    loaded.Location.Should().Be("https://example.com/c");
    loaded.AllowInsecure.Should().BeTrue();
    loaded.Token.Should().Be("secret");
  }

  [Fact]
  public void TheTokenRulesAreKeepClearAndSet() {
    var store = new GamepackCatalogSettingsStore(_root);
    store.Save("https://example.com/c", false, "one");

    store.Save("https://example.com/c", false, null).Token.Should().Be("one");
    store.Save("https://example.com/c", false, "two").Token.Should().Be("two");
    store.Save("https://example.com/c", false, string.Empty).Token.Should().BeNull();
    store.Load().Token.Should().BeNull();
  }

  [Fact]
  public void ABadLocationIsRefusedAndTheFileStaysAsItWas() {
    var store = new GamepackCatalogSettingsStore(_root);
    store.Save("https://example.com/c", false, "tok");

    var act = () => store.Save("ftp://nope", false, null);

    act.Should().Throw<CatalogException>().Which.Code.Should().Be(CatalogFailure.InvalidLocation);
    store.Load().Location.Should().Be("https://example.com/c");
  }

  [Fact]
  public void AnEmptyLocationClearsIt() {
    var store = new GamepackCatalogSettingsStore(_root);
    store.Save("https://example.com/c", false, null);

    store.Save("  ", false, null).Location.Should().BeNull();
  }

  [Fact]
  public void ADamagedFileGivesEmptySettings() {
    File.WriteAllText(Path.Combine(_root, GamepackCatalogSettingsStore.FileName), "{ not json");

    new GamepackCatalogSettingsStore(_root).Load().Location.Should().BeNull();
  }

  [Fact]
  public void AChangeByOneStoreShowsInAnotherWithoutARestart() {
    var reader = new GamepackCatalogSettingsStore(_root);
    var writer = new GamepackCatalogSettingsStore(_root);
    reader.Load().Location.Should().BeNull();

    writer.Save("https://example.com/c", false, null);

    reader.Load().Location.Should().Be("https://example.com/c");
  }
}
