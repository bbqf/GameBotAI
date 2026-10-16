using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 147 (T027): a missing index.json gives index_invalid with HTTP 502 and no change (FR-013, SC-003).</summary>
public sealed class GamepackCatalogMissingIndexContractTests {
  [Fact]
  public async Task AMissingIndexInAFolderGivesIndexInvalidAndNothingChanges() {
    using var host = new GamepackContractHost();
    var folder = Path.Combine(host.Root, "empty-catalog");
    Directory.CreateDirectory(folder);
    await host.SetCatalogAsync(folder);
    var before = await host.StateSnapshotAsync();

    var response = await host.CatalogListAsync();

    response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    (await GamepackContractHost.CodeOfAsync(response)).Should().Be("index_invalid");
    (await response.Content.ReadAsStringAsync()).Should().Contain("index.json");
    (await host.StateSnapshotAsync()).Should().Be(before);
  }

  [Fact]
  public async Task AMissingIndexOnAWebCatalogGivesIndexInvalidAndNothingChanges() {
    await using var server = await CatalogTestServer.StartAsync();
    using var host = new GamepackContractHost();
    await host.SetCatalogAsync(server.BaseUrl + "/gp");
    var before = await host.StateSnapshotAsync();

    var response = await host.CatalogListAsync();

    response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    (await GamepackContractHost.CodeOfAsync(response)).Should().Be("index_invalid");
    (await host.StateSnapshotAsync()).Should().Be(before);
  }
}
