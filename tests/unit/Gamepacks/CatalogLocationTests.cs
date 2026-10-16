using System;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 147 (T007): the scheme rules, the base address, and the safe path rules.</summary>
public sealed class CatalogLocationTests {
  [Theory]
  [InlineData("https://example.com/catalog")]
  [InlineData("http://localhost:8080/c")]
  [InlineData("http://127.0.0.5/c")]
  [InlineData("http://[::1]/c")]
  public void AnAllowedWebAddressParses(string location) {
    CatalogLocation.TryParse(location, allowInsecure: false, out var parsed, out _).Should().BeTrue();
    parsed!.Kind.Should().Be(CatalogKind.Web);
    parsed.WebBase!.AbsoluteUri.Should().EndWith("/");
  }

  [Fact]
  public void HttpForANonLoopbackHostNeedsAllowInsecure() {
    CatalogLocation.TryParse("http://example.com/c", false, out _, out _).Should().BeFalse();
    CatalogLocation.TryParse("http://example.com/c", true, out var parsed, out _).Should().BeTrue();
    parsed!.Kind.Should().Be(CatalogKind.Web);
  }

  [Theory]
  [InlineData("ftp://example.com/c")]
  [InlineData("file://host/share")]
  [InlineData("https://user:pw@example.com/c")]
  [InlineData("https://example.com/c?x=1")]
  [InlineData("relative/folder")]
  [InlineData("")]
  [InlineData(null)]
  public void ABadLocationIsRefused(string? location) {
    CatalogLocation.TryParse(location, true, out _, out var error).Should().BeFalse();
    error.Should().NotBeNullOrEmpty();
  }

  [Fact]
  public void AFullFolderPathParsesAsAFolder() {
    var folder = System.IO.Path.GetTempPath();
    CatalogLocation.TryParse(folder, false, out var parsed, out _).Should().BeTrue();
    parsed!.Kind.Should().Be(CatalogKind.Folder);
    parsed.Folder.Should().NotBeNullOrEmpty();
  }

  [Theory]
  [InlineData("a/b/c.json", true)]
  [InlineData("c.json", true)]
  [InlineData("../c.json", false)]
  [InlineData("a/../../c.json", false)]
  [InlineData("a/./c.json", false)]
  [InlineData("%2e%2e/c.json", false)]
  [InlineData("a/%2E%2E/c.json", false)]
  [InlineData("a%5Cb.json", false)]
  [InlineData("a\\b.json", false)]
  [InlineData("/abs/c.json", false)]
  [InlineData("C:/abs/c.json", false)]
  [InlineData("https://other.example/c.json", false)]
  [InlineData("//other.example/c.json", false)]
  [InlineData("a//c.json", false)]
  [InlineData("a/c.json?x=1", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  public void ARelativePathIsSafeOnlyWhenItStaysUnderTheBase(string? path, bool safe) {
    CatalogLocation.IsSafeRelativePath(path).Should().Be(safe);
  }

  [Fact]
  public void AWebFileResolvesUnderTheBasePath() {
    var baseUri = new Uri("https://example.com/catalog/");
    CatalogLocation.ResolveWeb(baseUri, "a/b.json")!.AbsoluteUri.Should().Be("https://example.com/catalog/a/b.json");
    CatalogLocation.ResolveWeb(baseUri, "../b.json").Should().BeNull();
    CatalogLocation.ResolveWeb(baseUri, "https://evil.example/b.json").Should().BeNull();
  }

  [Fact]
  public void AFolderFileResolvesUnderTheFolder() {
    var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GameBotCatalogLoc");
    CatalogLocation.ResolveFolder(folder, "a/b.json").Should().StartWith(folder);
    CatalogLocation.ResolveFolder(folder, "../b.json").Should().BeNull();
    CatalogLocation.ResolveFolder(folder, "C:\\Windows\\win.ini").Should().BeNull();
  }

  [Theory]
  [InlineData("localhost", true)]
  [InlineData("127.0.0.1", true)]
  [InlineData("127.9.9.9", true)]
  [InlineData("[::1]", true)]
  [InlineData("example.com", false)]
  [InlineData("10.0.0.1", false)]
  public void LoopbackHostsAreRecognized(string host, bool loopback) {
    CatalogLocation.IsLoopback(new Uri("http://" + host + "/")).Should().Be(loopback);
  }
}
