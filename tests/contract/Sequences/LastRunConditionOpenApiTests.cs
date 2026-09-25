using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 105 (FR-013): the OpenAPI document describes the <c>lastRun</c> condition.
/// </summary>
public sealed class LastRunConditionOpenApiTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<JsonElement> ReadDocumentAsync() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.Clone();
  }

  private static JsonElement Schemas(JsonElement document) => document.GetProperty("components").GetProperty("schemas");

  [Fact]
  public async Task TheLastRunConditionSchemaExistsAndTheDiscriminatorMapsIt() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);
    var schemas = Schemas(document);

    schemas.TryGetProperty("LastRunCondition", out _).Should().BeTrue();

    schemas.TryGetProperty("LastRunConditionContract", out _).Should().BeTrue();

    // The same rule as the composite kinds (SequencePerStepConditionsOpenApiTests): when the document
    // publishes a discriminator block, it must map lastRun; when it does not, the schema itself must
    // name its discriminator value, or an author cannot learn it without reading source.
    var condition = schemas.GetProperty("SequenceStepCondition");
    if (condition.TryGetProperty("discriminator", out var discriminator)
        && discriminator.TryGetProperty("mapping", out var mapping)) {
      mapping.GetProperty("lastRun").GetString().Should().EndWith("LastRunConditionContract");
    }
    else {
      schemas.GetProperty("LastRunCondition").GetProperty("description").GetString()
        .Should().Contain("type: \"lastRun\"");
    }
  }

  [Fact]
  public async Task EachFieldHasADescriptionAndStatusListsItsValues() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);
    var properties = Schemas(document).GetProperty("LastRunCondition").GetProperty("properties");

    foreach (var field in new[] { "sequence", "status", "since", "within", "negate" }) {
      properties.GetProperty(field).GetProperty("description").GetString()
        .Should().NotBeNullOrWhiteSpace($"'{field}' must have a description");
    }

    properties.GetProperty("status").GetProperty("enum").EnumerateArray().Select(v => v.GetString())
      .Should().Equal("success", "failure", "cancelled");
  }

  [Fact]
  public async Task SinceAndWithinHaveTheContractPatterns() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);
    var properties = Schemas(document).GetProperty("LastRunCondition").GetProperty("properties");

    properties.GetProperty("since").GetProperty("pattern").GetString().Should().Be(@"^([01]\d|2[0-3]):[0-5]\d$");
    properties.GetProperty("within").GetProperty("pattern").GetString()
      .Should().Be(@"^(?:\d{1,3}\.)?\d{1,4}:[0-5]\d:[0-5]\d$");
  }

  [Fact]
  public async Task TheSchemaDescriptionStatesTheTwoRules() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var description = Schemas(document).GetProperty("LastRunCondition").GetProperty("description").GetString();

    description.Should().Contain("exactly one of since or within").And.Contain("has no queue").And.Contain("false");
  }
}
