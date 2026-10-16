using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 147 (T023, T027): the list route, the installed marker, and the error codes.</summary>
public sealed class GamepackCatalogListContractTests {
  private static CatalogFixture TwoPacks() => new CatalogFixture()
    .Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0"), "First.")
    .Add("pns-daily", "1.2.0", CatalogFixture.SimplePack("pns-daily", "1.2.0"), "Fix the bank step.")
    .Add("pns-daily", "1.10.0", CatalogFixture.SimplePack("pns-daily", "1.10.0"), "Tenth.")
    .Add("other", "0.1.0", CatalogFixture.SimplePack("other", "0.1.0", "Other seq"), "Other.");

  [Fact]
  public async Task TheListShowsAllVersionsWithChangelogAndTheInstalledMarker() {
    using var host = new GamepackContractHost();
    var folder = Path.Combine(host.Root, "catalog");
    TwoPacks().WriteTo(folder);
    await host.SetCatalogAsync(folder);
    (await host.InstallAsync(CatalogFixture.SimplePack("pns-daily", "1.2.0"))).EnsureSuccessStatusCode();

    var response = await host.CatalogListAsync();

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var packs = (await GamepackContractHost.ReadAsync(response)).GetProperty("gamepacks");
    packs.GetArrayLength().Should().Be(2);
    var daily = packs.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "pns-daily");
    daily.GetProperty("installedVersion").GetString().Should().Be("1.2.0");
    var versions = daily.GetProperty("versions").EnumerateArray().ToList();
    versions.Select(v => v.GetProperty("version").GetString()).Should().Equal("1.10.0", "1.2.0", "1.0.0");
    versions.Select(v => v.GetProperty("installed").GetBoolean()).Should().Equal(false, true, false);
    versions.Select(v => v.GetProperty("updateAvailable").GetBoolean()).Should().Equal(true, false, false);
    versions[1].GetProperty("changelog").GetString().Should().Be("Fix the bank step.");
    var other = packs.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "other");
    other.GetProperty("installedVersion").ValueKind.Should().Be(JsonValueKind.Null);
    other.GetProperty("versions")[0].GetProperty("updateAvailable").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task NoLocationGivesNotConfigured() {
    using var host = new GamepackContractHost();

    var response = await host.CatalogListAsync();

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await GamepackContractHost.CodeOfAsync(response)).Should().Be("not_configured");
  }

  [Fact]
  public async Task ABadIndexGivesIndexInvalid() {
    using var host = new GamepackContractHost();
    var folder = Path.Combine(host.Root, "catalog");
    Directory.CreateDirectory(folder);
    await File.WriteAllTextAsync(Path.Combine(folder, "index.json"), "not json");
    await host.SetCatalogAsync(folder);

    var response = await host.CatalogListAsync();

    response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    (await GamepackContractHost.CodeOfAsync(response)).Should().Be("index_invalid");
  }

  [Fact]
  public async Task AnUnreachableCatalogGivesUnreachable() {
    using var host = new GamepackContractHost();
    await host.SetCatalogAsync("https://127.0.0.1:1/gp");

    var response = await host.CatalogListAsync();

    response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    (await GamepackContractHost.CodeOfAsync(response)).Should().Be("unreachable");
  }
}
