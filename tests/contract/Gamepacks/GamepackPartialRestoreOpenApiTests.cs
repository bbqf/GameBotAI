using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Issue #333 (T033): the OpenAPI document describes the file error text and failedRestoreKeys.</summary>
public sealed class GamepackPartialRestoreOpenApiTests {
  [Fact]
  public async Task TheRoutesDescribeTheFileErrorAndTheKeyList() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var paths = document.RootElement.GetProperty("paths");

    var install = paths.GetProperty("/api/gamepacks/install").GetProperty("post").GetProperty("description").GetString();
    install.Should().Contain("failedRestoreKeys").And.Contain("Cannot replace file").And.Contain("partial_restore");

    paths.GetProperty("/api/gamepacks/{name}/rollback").GetProperty("post").GetProperty("description").GetString()
      .Should().Contain("failedRestoreKeys");
    paths.GetProperty("/api/gamepacks/takeover-backups/{backupId}/restore").GetProperty("post").GetProperty("description").GetString()
      .Should().Contain("failedRestoreKeys");
  }
}
