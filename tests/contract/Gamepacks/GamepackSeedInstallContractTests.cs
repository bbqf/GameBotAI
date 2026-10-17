using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 150 (T016): the install routes create the seed templates of a first install.</summary>
public sealed class GamepackSeedInstallContractTests {
  [Fact]
  public async Task AFirstInstallCreatesTheSeedAndListsIt() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(GamepackSeedContractKit.Pack(seeds: GamepackSeedContractKit.Daily()));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    GamepackSeedContractKit.Names(body.GetProperty("seedTemplatesCreated")).Should().Equal("Daily Help");
    body.GetProperty("seedTemplatesSkipped").GetArrayLength().Should().Be(0);
    var templates = await GamepackSeedContractKit.TemplatesAsync(host);
    templates.Should().ContainSingle().Which.Name.Should().Be("Daily Help");
    var detail = await host.GetJsonAsync("/api/queue-templates/" + templates[0].Id);
    detail.GetProperty("entries")[0].GetProperty("sequenceId").GetString().Should().Be(await host.SequenceIdAsync("Help All"));
    detail.GetProperty("entries")[0].GetProperty("timerTimeOfDay").GetString().Should().StartWith("03:30");
  }

  [Fact]
  public async Task AUsedNameGivesSkippedExistsAndLeavesTheTemplate() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(GamepackTestKit.Pack("base", "1", sequences: new[] { GamepackTestKit.Seq("b1", "Base Seq") }))).EnsureSuccessStatusCode();
    await GamepackSeedContractKit.MakeTemplateAsync(host, "daily help", "Base Seq");

    var response = await host.InstallAsync(GamepackTestKit.Pack("pns-daily", "1.0.0", seedTemplates: new[] { GamepackTestKit.Seed("Daily Help", GamepackTestKit.SeedEntry("Base Seq")) }));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("seedTemplatesCreated").GetArrayLength().Should().Be(0);
    GamepackSeedContractKit.Skips(body.GetProperty("seedTemplatesSkipped")).Should().Equal(("Daily Help", "skipped_exists"));
    var templates = await GamepackSeedContractKit.TemplatesAsync(host);
    templates.Should().ContainSingle().Which.Name.Should().Be("daily help");
  }

  [Fact]
  public async Task AFileWithoutSeedsGivesBothListsEmptyAndTheSameCounts() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(GamepackSeedContractKit.Pack());

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("seedTemplatesCreated").GetArrayLength().Should().Be(0);
    body.GetProperty("seedTemplatesSkipped").GetArrayLength().Should().Be(0);
    body.GetProperty("created").GetInt32().Should().Be(1);
    (await GamepackSeedContractKit.TemplatesAsync(host)).Should().BeEmpty();
  }

  [Fact]
  public async Task TheCatalogInstallCreatesTheSeedToo() {
    using var host = new GamepackContractHost();
    var folder = Path.Combine(host.Root, "catalog");
    new CatalogFixture().Add("pns-daily", "1.0.0", GamepackSeedContractKit.Pack(seeds: GamepackSeedContractKit.Daily())).WriteTo(folder);
    await host.SetCatalogAsync(folder);

    var response = await host.CatalogInstallAsync("pns-daily", "1.0.0");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    GamepackSeedContractKit.Names(body.GetProperty("seedTemplatesCreated")).Should().Equal("Daily Help");
    body.GetProperty("seedTemplatesSkipped").GetArrayLength().Should().Be(0);
    (await GamepackSeedContractKit.TemplatesAsync(host)).Should().ContainSingle();
  }
}
