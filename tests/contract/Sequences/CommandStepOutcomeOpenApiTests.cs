using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 167 (FR-011): the OpenAPI document describes the commandStepOutcome condition.</summary>
public sealed class CommandStepOutcomeOpenApiTests {
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

    schemas.TryGetProperty("CommandStepOutcomeCondition", out var schema).Should().BeTrue();
    schemas.TryGetProperty("CommandStepOutcomeConditionContract", out _).Should().BeTrue();

    var condition = schemas.GetProperty("SequenceStepCondition");
    if (condition.TryGetProperty("discriminator", out var discriminator)
        && discriminator.TryGetProperty("mapping", out var mapping)) {
      mapping.GetProperty("commandStepOutcome").GetString().Should().EndWith("CommandStepOutcomeConditionContract");
    }
    else {
      schema.GetProperty("description").GetString().Should().Contain("type: \"commandStepOutcome\"");
    }
  }

  [Fact]
  public async Task ExpectedStateListsTheFourStatesAndTheOrderHasAMinimumOfOne() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);
    var properties = schemas.GetProperty("CommandStepOutcomeCondition").GetProperty("properties");

    properties.GetProperty("expectedState").GetProperty("enum").EnumerateArray().Select(v => v.GetString())
      .Should().Equal("executed", "skipped_detection_failed", "completed_timeout", "image_detected");
    properties.GetProperty("commandStepOrder").GetProperty("minimum").GetInt32().Should().Be(1);
  }

  [Fact]
  public async Task EachFieldHasADescription() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);
    var schema = schemas.GetProperty("CommandStepOutcomeCondition");

    foreach (var field in new[] { "stepRef", "commandStepOrder", "expectedState", "negate" }) {
      schema.GetProperty("properties").GetProperty(field).GetProperty("description").GetString()
        .Should().NotBeNullOrWhiteSpace($"'{field}' must have a description");
    }

    schema.GetProperty("description").GetString().Should().Contain("missing outcome").And.Contain("dry run");
  }
}
