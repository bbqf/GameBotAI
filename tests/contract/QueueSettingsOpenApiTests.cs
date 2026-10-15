using System.Net;
using System.Text.Json;
using FluentAssertions;
using GameBot.ContractTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.QueueSettings;

/// <summary>Feature 145 (T027): the OpenAPI document shows the queue settings routes and describes the new types.</summary>
public sealed class QueueSettingsOpenApiTests {
  private static async Task<JsonElement> ReadDocumentAsync() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
  }

  [Fact]
  public async Task TheTwoRoutesAreInTheDocumentWithTheirStatusCodes() {
    var document = await ReadDocumentAsync();
    var paths = document.GetProperty("paths");

    var export = paths.GetProperty("/api/queue-settings/export").GetProperty("get");
    export.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
    export.GetProperty("description").GetString().Should().Contain("no id");
    var import = paths.GetProperty("/api/queue-settings/import").GetProperty("post");
    import.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
    var responses = import.GetProperty("responses");
    foreach (var status in new[] { "200", "400", "413", "500" }) {
      responses.TryGetProperty(status, out _).Should().BeTrue("the import route lists status " + status);
    }

    import.GetProperty("description").GetString().Should().Contain("500").And.Contain("same document again");
  }

  [Fact]
  public async Task TheNewTypesHaveDescriptions() {
    var document = await ReadDocumentAsync();
    var schemas = document.GetProperty("components").GetProperty("schemas");

    schemas.GetProperty("SettingsDocument").GetProperty("description").GetString().Should().Contain("formatVersion");
    var report = schemas.GetProperty("ImportReport");
    report.GetProperty("description").GetString().Should().Contain("500").And.Contain("notApplied");
    report.GetProperty("properties").TryGetProperty("failed", out _).Should().BeTrue();
    report.GetProperty("properties").TryGetProperty("notApplied", out _).Should().BeTrue();
    schemas.GetProperty("ImportItem").GetProperty("properties").GetProperty("restartRequired").GetProperty("description").GetString()
      .Should().Contain("never stops");
    schemas.GetProperty("UnresolvedReference").GetProperty("description").GetString().Should().Contain("missing");
    schemas.GetProperty("NameReference").GetProperty("properties").GetProperty("usedBy").GetProperty("description").GetString()
      .Should().Contain("type:name");
  }
}
