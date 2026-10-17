using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 150 (T022): only a first install touches seeds.</summary>
public sealed class GamepackSeedLaterInstallContractTests {
  private static async Task<GamepackContractHost> InstalledAsync() {
    var host = new GamepackContractHost();
    (await host.InstallAsync(GamepackSeedContractKit.Pack("1.0.0", seeds: GamepackSeedContractKit.Daily()))).StatusCode.Should().Be(HttpStatusCode.OK);
    return host;
  }

  [Fact]
  public async Task AUserChangeToTheTemplateIsKeptWhenALaterVersionChangesTheSeed() {
    using var host = await InstalledAsync();
    var template = (await GamepackSeedContractKit.TemplatesAsync(host)).Single();
    var changed = await host.Client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Daily Help", overwrite = true, entries = Array.Empty<object>() });
    changed.EnsureSuccessStatusCode();
    var v2 = GamepackSeedContractKit.Pack("2.0.0", seeds: GamepackTestKit.Seed("Daily Help", GamepackTestKit.SeedEntry("Help All", "EveryStep")));

    var response = await host.InstallAsync(v2);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("seedTemplatesCreated").GetArrayLength().Should().Be(0);
    body.GetProperty("seedTemplatesSkipped").GetArrayLength().Should().Be(0);
    var after = await host.GetJsonAsync("/api/queue-templates/" + template.Id);
    after.GetProperty("entries").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task ADeletedTemplateIsNotCreatedAgainByALaterInstall() {
    using var host = await InstalledAsync();
    var template = (await GamepackSeedContractKit.TemplatesAsync(host)).Single();
    (await host.Client.DeleteAsync(new Uri("/api/queue-templates/" + template.Id, UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var response = await host.InstallAsync(GamepackSeedContractKit.Pack("1.1.0", seeds: GamepackSeedContractKit.Daily()));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackSeedContractKit.TemplatesAsync(host)).Should().BeEmpty();
  }

  [Fact]
  public async Task ANewSeedInALaterVersionIsNotCreated() {
    using var host = await InstalledAsync();
    var v2 = GamepackSeedContractKit.Pack("2.0.0", seeds: new[] { GamepackSeedContractKit.Daily(), GamepackTestKit.Seed("Brand New", GamepackTestKit.SeedEntry("Help All")) });

    var response = await host.InstallAsync(v2);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("seedTemplatesCreated").GetArrayLength().Should().Be(0);
    (await GamepackSeedContractKit.TemplatesAsync(host)).Select(t => t.Name).Should().Equal("Daily Help");
  }

  [Fact]
  public async Task ARollbackKeepsTheTemplateAndGivesNoSeedLists() {
    using var host = await InstalledAsync();
    (await host.InstallAsync(GamepackSeedContractKit.Pack("2.0.0"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.RollbackAsync("pns-daily");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackSeedContractKit.TemplatesAsync(host)).Select(t => t.Name).Should().Equal("Daily Help");
  }

  [Fact]
  public async Task TheSettingsExportListsTheSeedAsAnOrdinaryTemplate() {
    using var host = await InstalledAsync();

    var export = await host.GetJsonAsync("/api/queue-settings/export");

    var template = export.GetProperty("queueTemplates").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Daily Help");
    template.TryGetProperty("gamepack", out _).Should().BeFalse("a seed has no link to the gamepack");
    template.GetProperty("entries")[0].GetProperty("sequence").GetString().Should().Be("Help All");
  }

  [Fact]
  public async Task AManagedUpdateGivesBothListsPresentAndEmpty() {
    using var host = new ManagedContractHost();
    (await host.Host.InstallAsync(GamepackSeedContractKit.Pack("1.0.0", seeds: GamepackSeedContractKit.Daily()))).EnsureSuccessStatusCode();

    var response = await host.UpdateAsync(GamepackSeedContractKit.Pack("2.0.0", seeds: GamepackTestKit.Seed("New Seed", GamepackTestKit.SeedEntry("Help All"))));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("seedTemplatesCreated").GetArrayLength().Should().Be(0);
    body.GetProperty("seedTemplatesSkipped").GetArrayLength().Should().Be(0);
    (await GamepackSeedContractKit.TemplatesAsync(host.Host)).Select(t => t.Name).Should().Equal("Daily Help");
  }
}
