using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>Issue #338 (FR-012): the OpenAPI document describes the ocrText condition.</summary>
public sealed class OcrTextConditionOpenApiTests {
  private static async Task<JsonElement> ReadSchemasAsync() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.GetProperty("components").GetProperty("schemas").Clone();
  }

  [Fact]
  public async Task TheSchemaExistsAndNamesItsDiscriminator() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);

    schemas.TryGetProperty("OcrTextCondition", out var schema).Should().BeTrue();
    schemas.TryGetProperty("OcrTextConditionContract", out _).Should().BeTrue();

    var condition = schemas.GetProperty("SequenceStepCondition");
    if (condition.TryGetProperty("discriminator", out var discriminator)
        && discriminator.TryGetProperty("mapping", out var mapping)) {
      mapping.GetProperty("ocrText").GetString().Should().EndWith("OcrTextConditionContract");
    }
    else {
      schema.GetProperty("description").GetString().Should().Contain("type: \"ocrText\"");
    }
  }

  [Fact]
  public async Task EachFieldHasADescriptionAndTheSchemaListsTheReasonCodes() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);
    var schema = schemas.GetProperty("OcrTextCondition");
    var properties = schema.GetProperty("properties");

    // The region property is a reference to the shared PixelRegion schema, so it has no description of its own.
    foreach (var field in new[] { "expected", "pattern", "parser", "negate" }) {
      properties.GetProperty(field).GetProperty("description").GetString()
        .Should().NotBeNullOrWhiteSpace($"'{field}' must have a description");
    }

    var description = schema.GetProperty("description").GetString();
    description.Should().Contain("exactly one of expected and pattern")
      .And.Contain("no-session").And.Contain("parse-failed").And.Contain("web UI does not know");
  }
}
