using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 147 (T030): the install route.</summary>
public sealed class GamepackCatalogInstallContractTests {
  [Fact]
  public async Task AnUnlistedNameOrVersionGivesEntryNotListedBeforeAnyFileRequest() {
    await using var server = await CatalogTestServer.StartAsync();
    new CatalogFixture().Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0")).ServeFrom(server, "/gp");
    using var host = new GamepackContractHost();
    await host.SetCatalogAsync(server.BaseUrl + "/gp");

    foreach (var (name, version) in new[] { ("nope", "1.0.0"), ("pns-daily", "9.9.9"), ("pns-daily", "not-a-version") }) {
      var response = await host.CatalogInstallAsync(name, version);

      response.StatusCode.Should().Be(HttpStatusCode.NotFound);
      (await GamepackContractHost.CodeOfAsync(response)).Should().Be("entry_not_listed");
    }

    server.Paths.Should().OnlyContain(p => p == "/gp/index.json");
  }

  [Fact]
  public async Task AListedFileThatTheServerAnswersWithNotFoundGivesNotFound() {
    await using var server = await CatalogTestServer.StartAsync();
    var fixture = new CatalogFixture().Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0"));
    server.Set("/gp/index.json", fixture.IndexJson());
    using var host = new GamepackContractHost();
    await host.SetCatalogAsync(server.BaseUrl + "/gp");

    var response = await host.CatalogInstallAsync("pns-daily", "1.0.0");

    response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    (await GamepackContractHost.CodeOfAsync(response)).Should().Be("not_found");
  }

  [Fact]
  public async Task AnInstallGivesTheSameBodyShapeAsTheManualRoute() {
    using var host = new GamepackContractHost();
    var folder = Path.Combine(host.Root, "catalog");
    new CatalogFixture().Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0"), "First.").WriteTo(folder);
    await host.SetCatalogAsync(folder);

    var dry = await host.CatalogInstallAsync("pns-daily", "1.0.0", dryRun: true);
    var real = await host.CatalogInstallAsync("pns-daily", "1.0.0");

    dry.StatusCode.Should().Be(HttpStatusCode.OK);
    var dryBody = await GamepackContractHost.ReadAsync(dry);
    dryBody.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    real.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(real);
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.GetProperty("gamepack").GetProperty("name").GetString().Should().Be("pns-daily");
    body.GetProperty("created").GetInt32().Should().BeGreaterThan(0);
    body.TryGetProperty("signature", out _).Should().BeTrue();
    (await host.GetJsonAsync("/api/gamepacks")).EnumerateArray().Select(g => g.GetProperty("name").GetString()).Should().Contain("pns-daily");
  }

  [Fact]
  public async Task NoLocationGivesNotConfigured() {
    using var host = new GamepackContractHost();

    var response = await host.CatalogInstallAsync("pns-daily", "1.0.0");

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await GamepackContractHost.CodeOfAsync(response)).Should().Be("not_configured");
  }

  [Fact]
  public async Task AMissingNameOrVersionGivesBadRequest() {
    using var host = new GamepackContractHost();

    (await host.CatalogInstallAsync(null, "1.0.0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await host.CatalogInstallAsync("pns-daily", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }
}
