using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 147 (T009): the index parser rules.</summary>
public sealed class GamepackCatalogIndexParserTests {
  private static readonly string Sha = new string('a', 64);

  private static string Index(string versions, string name = "pns-daily") =>
    "{\"formatVersion\":1,\"gamepacks\":[{\"name\":\"" + name + "\",\"versions\":[" + versions + "]}]}";

  [Fact]
  public void AValidEntryParsesWithAllFields() {
    var json = Index("{\"version\":\"1.2.0\",\"changelog\":\"Fix.\",\"file\":\"p/1.2.0/p.json\",\"signature\":\"p/1.2.0/p.sig.json\",\"sha256\":\"" + Sha + "\"}");

    var entry = GamepackCatalogIndexParser.Parse(json).Single();

    entry.Name.Should().Be("pns-daily");
    entry.Version.Should().Be("1.2.0");
    entry.Changelog.Should().Be("Fix.");
    entry.File.Should().Be("p/1.2.0/p.json");
    entry.Signature.Should().Be("p/1.2.0/p.sig.json");
    entry.Sha256.Should().Be(Sha);
  }

  [Fact]
  public void OptionalFieldsMayBeMissing() {
    var entry = GamepackCatalogIndexParser.Parse(Index("{\"version\":\"1.0.0\",\"file\":\"a.json\"}")).Single();

    entry.Changelog.Should().BeEmpty();
    entry.Signature.Should().BeNull();
    entry.Sha256.Should().BeNull();
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("{\"formatVersion\":2,\"gamepacks\":[]}")]
  [InlineData("{\"gamepacks\":[]}")]
  [InlineData("{\"formatVersion\":1}")]
  public void ABadDocumentGivesIndexInvalid(string json) {
    var act = () => GamepackCatalogIndexParser.Parse(json);

    act.Should().Throw<CatalogException>().Which.Code.Should().Be(CatalogFailure.IndexInvalid);
  }

  [Fact]
  public void ABadEntryIsSkippedAndTheOthersStay() {
    var json = Index(
      "{\"version\":\"bad\",\"file\":\"a.json\"},"
      + "{\"version\":\"1.0.0\",\"file\":\"../a.json\"},"
      + "{\"version\":\"1.1.0\",\"file\":\"a.json\",\"sha256\":\"xyz\"},"
      + "{\"version\":\"1.2.0\",\"file\":\"a.json\",\"signature\":\"/abs.sig\"},"
      + "{\"version\":\"1.3.0\",\"file\":\"ok.json\"}");

    var entries = GamepackCatalogIndexParser.Parse(json);

    entries.Select(e => e.Version).Should().Equal("1.3.0");
  }

  [Fact]
  public void ABadNameIsSkipped() {
    var json = Index("{\"version\":\"1.0.0\",\"file\":\"a.json\"}", name: "..bad name");

    GamepackCatalogIndexParser.Parse(json).Should().BeEmpty();
  }

  [Fact]
  public void TheFirstDuplicateWins() {
    var json = Index("{\"version\":\"1.0.0\",\"changelog\":\"first\",\"file\":\"a.json\"},{\"version\":\"1.0.0\",\"changelog\":\"second\",\"file\":\"b.json\"}");

    var entries = GamepackCatalogIndexParser.Parse(json);

    entries.Should().ContainSingle().Which.Changelog.Should().Be("first");
  }

  [Fact]
  public void VersionsCompareByNumber() {
    CatalogVersion.TryParse("1.10.0", out var newer).Should().BeTrue();
    CatalogVersion.TryParse("1.9.0", out var older).Should().BeTrue();
    CatalogVersion.TryParse("1.2", out var two).Should().BeTrue();
    CatalogVersion.TryParse("1.2.0", out var three).Should().BeTrue();

    (newer > older).Should().BeTrue();
    two.Should().Be(three);
    CatalogVersion.TryParse("1.x", out _).Should().BeFalse();
    CatalogVersion.TryParse("-1.0", out _).Should().BeFalse();
  }
}
