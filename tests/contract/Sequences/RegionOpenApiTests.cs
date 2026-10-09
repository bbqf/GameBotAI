using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 130 (FR-010): the OpenAPI document shows the optional region and its schema.</summary>
public sealed class RegionOpenApiTests {
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

  [Theory]
  [InlineData("ImageVisibleCondition")]
  [InlineData("DetectionTarget")]
  public async Task ObjectListsTheOptionalRegionProperty(string schemaName) {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);

    var schema = schemas.GetProperty(schemaName);

    schema.GetProperty("properties").TryGetProperty("region", out _).Should().BeTrue();
    if (schema.TryGetProperty("required", out var required)) {
      required.EnumerateArray().Select(item => item.GetString()).Should().NotContain("region");
    }
  }

  [Fact]
  public async Task PixelRegionSchemaListsTheFourFields() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);

    var schema = schemas.GetProperty("PixelRegion");
    var properties = schema.GetProperty("properties");

    foreach (var name in new[] { "x", "y", "width", "height" }) {
      properties.GetProperty(name).GetProperty("type").GetString().Should().Be("integer");
    }
    schema.GetProperty("description").GetString().Should().Contain("capture pixels").And.Contain("more than 0");
  }

  [Fact]
  public async Task WaitForImagePayloadTextMentionsTheRegion() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var text = await client.GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);

    text.Should().Contain("region (object, optional: x, y, width, height in capture pixels");
  }
}
