using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 143 (T045): the OpenAPI document shows the gamepack routes and the owner schema.</summary>
public sealed class GamepackOpenApiTests {
  private static async Task<JsonElement> ReadDocumentAsync() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
  }

  [Fact]
  public async Task TheThreeRoutesAreInTheDocument() {
    var document = await ReadDocumentAsync();
    var paths = document.GetProperty("paths");

    paths.GetProperty("/api/gamepacks/install").TryGetProperty("post", out _).Should().BeTrue();
    paths.GetProperty("/api/gamepacks").TryGetProperty("get", out _).Should().BeTrue();
    paths.GetProperty("/api/gamepacks/{name}/rollback").TryGetProperty("post", out _).Should().BeTrue();

    var install = paths.GetProperty("/api/gamepacks/install").GetProperty("post");
    install.GetProperty("parameters").EnumerateArray().Should().Contain(p => p.GetProperty("name").GetString() == "dryRun");
    var responses = install.GetProperty("responses");
    foreach (var status in new[] { "200", "400", "413", "422", "500" }) {
      responses.TryGetProperty(status, out _).Should().BeTrue("the install route lists status " + status);
    }
  }

  [Fact]
  public async Task TheFailureExplanationMembersAreDescribed() {
    var document = await ReadDocumentAsync();
    var schemas = document.GetProperty("components").GetProperty("schemas");

    var response = schemas.GetProperty("GamepackInstallResponse").GetProperty("properties");
    response.GetProperty("summary").TryGetProperty("$ref", out _).Should().BeTrue();
    schemas.GetProperty("GamepackInstallResponse").GetProperty("properties").GetProperty("wouldInstallWithTakeOver")
      .GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    var problem = schemas.GetProperty("GamepackProblemDto").GetProperty("properties");
    problem.GetProperty("takeOverResolves").GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    problem.GetProperty("remedy").GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();

    foreach (var name in new[] { "GamepackInstallSummaryDto", "GamepackInstallSummaryEntryDto" }) {
      schemas.GetProperty(name).GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }

    var entry = schemas.GetProperty("GamepackInstallSummaryEntryDto").GetProperty("properties");
    foreach (var member in new[] { "category", "count", "label", "remedy", "resolvedByTakeOver" }) {
      entry.GetProperty(member).GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }
  }

  [Fact]
  public async Task TheRemoveRouteIsDescribed() {
    var document = await ReadDocumentAsync();

    var remove = document.GetProperty("paths").GetProperty("/api/gamepacks/{name}").GetProperty("delete");

    remove.GetProperty("operationId").GetString().Should().Be("RemoveGamepack");
    remove.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
    remove.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    var responses = remove.GetProperty("responses");
    foreach (var status in new[] { "204", "404", "500" }) {
      responses.TryGetProperty(status, out _).Should().BeTrue("the remove route lists status " + status);
    }
  }

  [Fact]
  public async Task TheOwnerSchemaAndTheOwnerMembersAreDescribed() {
    var document = await ReadDocumentAsync();
    var schemas = document.GetProperty("components").GetProperty("schemas");

    var owner = schemas.GetProperty("GamepackOwnerDto");
    owner.GetProperty("description").GetString().Should().Contain("read-only");
    owner.GetProperty("properties").GetProperty("kind").GetProperty("enum").GetArrayLength().Should().Be(2);

    schemas.GetProperty("CommandResponse").GetProperty("properties").TryGetProperty("owner", out _).Should().BeTrue();
    schemas.GetProperty("GamepackProblemDto").GetProperty("properties").GetProperty("owner").GetProperty("description").GetString()
      .Should().Contain("user");
    schemas.TryGetProperty("GamepackInstallResponse", out _).Should().BeTrue();
    schemas.TryGetProperty("GamepackListItemDto", out _).Should().BeTrue();
  }
}
