using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 144: the route GET /api/version.</summary>
public sealed class ApiVersionContractTests {
  [Fact]
  public async Task TheRouteGivesTheFourValuesAndTwoCallsAreEqual() {
    using var host = new GamepackContractHost();

    var first = await host.Client.GetAsync(new Uri("/api/version", UriKind.Relative));
    var second = await host.Client.GetAsync(new Uri("/api/version", UriKind.Relative));

    first.StatusCode.Should().Be(HttpStatusCode.OK);
    var text = await first.Content.ReadAsStringAsync();
    text.Should().Be(await second.Content.ReadAsStringAsync());
    using var body = JsonDocument.Parse(text);
    body.RootElement.GetProperty("major").GetInt32().Should().Be(ApiVersion.Major);
    body.RootElement.GetProperty("minor").GetInt32().Should().Be(ApiVersion.Minor);
    body.RootElement.GetProperty("patch").GetInt32().Should().Be(ApiVersion.Patch);
    body.RootElement.GetProperty("schemaFormat").GetInt32().Should().Be(ApiVersion.SchemaFormat);
  }

  [Fact]
  public async Task ACallWithoutATokenFollowsTheApiTokenRule() {
    using var host = new GamepackContractHost();
    host.Client.DefaultRequestHeaders.Authorization = null;
    host.Client.DefaultRequestHeaders.Remove("Authorization");

    var response = await host.Client.GetAsync(new Uri("/api/version", UriKind.Relative));
    var other = await host.Client.GetAsync(new Uri("/api/gamepacks", UriKind.Relative));

    response.StatusCode.Should().Be(other.StatusCode);
  }

  [Fact]
  public async Task TheSwaggerDocumentDescribesTheRouteAndTheFields() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var root = document.RootElement;

    var operation = root.GetProperty("paths").GetProperty("/api/version").GetProperty("get");
    operation.GetProperty("operationId").GetString().Should().Be("GetApiVersion");
    operation.GetProperty("tags")[0].GetString().Should().Be("Version");

    var schema = root.GetProperty("components").GetProperty("schemas").GetProperty("ApiVersionResponse");
    foreach (var field in new[] { "major", "minor", "patch", "schemaFormat" }) {
      schema.GetProperty("properties").GetProperty(field).GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }
  }
}
