using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 148 (T051): the OpenAPI document shows the managed update routes and describes the new schemas.</summary>
public sealed class GamepackManagedOpenApiContractTests {
  private static async Task<JsonElement> ReadDocumentAsync() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
  }

  [Fact]
  public async Task TheTwoUpdateRoutesAreInTheDocument() {
    var paths = (await ReadDocumentAsync()).GetProperty("paths");

    var update = paths.GetProperty("/api/gamepacks/update").GetProperty("post");
    update.GetProperty("operationId").GetString().Should().Be("UpdateGamepack");
    update.GetProperty("description").GetString().Should().Contain("dryRun");
    foreach (var status in new[] { "200", "400", "409", "413", "422", "500" }) {
      update.GetProperty("responses").TryGetProperty(status, out _).Should().BeTrue("the update route lists " + status);
    }

    var catalog = paths.GetProperty("/api/gamepacks/catalog/update").GetProperty("post");
    catalog.GetProperty("operationId").GetString().Should().Be("UpdateGamepackFromCatalog");
  }

  [Fact]
  public async Task TheRollbackRouteDescribesTheManagedQuery() {
    var rollback = (await ReadDocumentAsync()).GetProperty("paths").GetProperty("/api/gamepacks/{name}/rollback").GetProperty("post");

    rollback.GetProperty("description").GetString().Should().Contain("managed=true");
    var names = rollback.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
    names.Should().Contain(new[] { "managed", "dryRun", "templates" });
  }

  [Fact]
  public async Task TheNewSchemasHaveDescriptions() {
    var schemas = (await ReadDocumentAsync()).GetProperty("components").GetProperty("schemas");

    var report = schemas.GetProperty("ManagedUpdateReportDto");
    report.GetProperty("description").GetString().Should().Contain("update_in_progress").And.Contain("60 seconds");
    schemas.GetProperty("ManagedRestoreDto").GetProperty("description").GetString().Should().Contain("failedObjects");
    report.GetProperty("properties").GetProperty("notRestartedQueues").GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    schemas.GetProperty("ManagedErrorDto").GetProperty("description").GetString().Should().Contain("reason");
    schemas.GetProperty("GamepackCatalogUpdateRequest").GetProperty("description").GetString().Should().Contain("templates");
  }
}
