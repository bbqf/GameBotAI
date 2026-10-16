using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 149 (T043): the catalog update route with the body field takeOver.</summary>
public sealed class GamepackCatalogUpdateTakeOverContractTests {
  private static async Task<ManagedContractHost> ArrangeAsync() {
    var host = new ManagedContractHost();
    var folder = Path.Combine(host.Host.Root, "catalog");
    new CatalogFixture().Add("pns", "1.0.0", ManagedContractHost.Pack("1.0.0", "alpha")).WriteTo(folder);
    await host.Host.SetCatalogAsync(folder);
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "alpha" })).StatusCode.Should().Be(HttpStatusCode.Created);
    (await host.Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name = "MainCmd-pns", steps = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.Created);
    return host;
  }

  [Fact]
  public async Task TheOptionOnTakesOverAndReportsTheFields() {
    using var host = await ArrangeAsync();

    var response = await host.CatalogUpdateAsync("pns", "1.0.0", takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("done");
    body.GetProperty("takenOver").GetInt32().Should().Be(2);
    body.GetProperty("takeOverBackupId").GetString().Should().MatchRegex(TakeOverContractKit.BackupIdPattern);
  }

  [Fact]
  public async Task TheOptionOffOrMissingKeepsTheConflict() {
    using var host = await ArrangeAsync();

    var response = await host.Client.PostAsJsonAsync(new Uri("/api/gamepacks/catalog/update", UriKind.Relative), new { name = "pns", version = "1.0.0" });

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
  }

  [Fact]
  public async Task ADryRunWithTheOptionOnListsTheObjects() {
    using var host = await ArrangeAsync();

    var response = await host.CatalogUpdateAsync("pns", "1.0.0", dryRun: true, takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("takenOver").GetInt32().Should().Be(2);
    body.GetProperty("takeOvers").EnumerateArray().Select(e => e.GetProperty("type").GetString()).Should().BeEquivalentTo("sequence", "command");
  }
}
