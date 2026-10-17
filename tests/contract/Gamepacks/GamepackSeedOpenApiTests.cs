using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 150 (T033, FR-012): the OpenAPI document describes the seed members.</summary>
public sealed class GamepackSeedOpenApiTests {
  private static async Task<JsonElement> ReadDocumentAsync() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
  }

  [Fact]
  public async Task TheResultMembersHaveDescriptions() {
    var schemas = (await ReadDocumentAsync()).GetProperty("components").GetProperty("schemas");

    foreach (var schemaName in new[] { "GamepackInstallResponse", "ManagedUpdateReportDto" }) {
      var properties = schemas.GetProperty(schemaName).GetProperty("properties");
      foreach (var name in new[] { "seedTemplatesCreated", "seedTemplatesSkipped" }) {
        properties.GetProperty(name).GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(schemaName + "." + name);
      }
    }

    var skip = schemas.GetProperty("GamepackSeedSkipDto");
    skip.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    skip.GetProperty("properties").GetProperty("name").GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    skip.GetProperty("properties").GetProperty("reason").GetProperty("description").GetString().Should().Contain("skipped_exists");
  }

  [Fact]
  public async Task TheInstallRouteAndTheFileDescribeTheSeeds() {
    var document = await ReadDocumentAsync();

    var install = document.GetProperty("paths").GetProperty("/api/gamepacks/install").GetProperty("post");
    install.GetProperty("description").GetString().Should().Contain("seedTemplates").And.Contain("seedTemplatesCreated").And.Contain("seedTemplatesSkipped");
    document.GetRawText().Should().Contain("Only a first install of the gamepack creates a seed");
  }
}
