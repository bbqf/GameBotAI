using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 193 (FR-014): the OpenAPI document describes the parameter condition.</summary>
public sealed class ParameterConditionOpenApiTests {
  private static async Task<JsonElement> ReadSchemasAsync() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.GetProperty("components").GetProperty("schemas").Clone();
  }

  [Fact]
  public async Task TheSchemaExistsAndNamesItsDiscriminator() {
    var schemas = await ReadSchemasAsync();

    schemas.TryGetProperty("ParameterCondition", out var schema).Should().BeTrue();
    schemas.TryGetProperty("ParameterConditionContract", out _).Should().BeTrue();

    var condition = schemas.GetProperty("SequenceStepCondition");
    if (condition.TryGetProperty("discriminator", out var discriminator)
        && discriminator.TryGetProperty("mapping", out var mapping)) {
      mapping.GetProperty("parameter").GetString().Should().EndWith("ParameterConditionContract");
    }
    else {
      schema.GetProperty("description").GetString().Should().Contain("type: \"parameter\"");
    }

    if (condition.TryGetProperty("oneOf", out var oneOf)) {
      oneOf.EnumerateArray().Select(item => item.GetProperty("$ref").GetString())
        .Should().Contain(reference => reference!.EndsWith("ParameterConditionContract", StringComparison.Ordinal));
    }
  }

  [Fact]
  public async Task TheFieldsTheOperatorEnumAndTheValueKindsAreDescribed() {
    var schemas = await ReadSchemasAsync();
    var schema = schemas.GetProperty("ParameterCondition");
    var properties = schema.GetProperty("properties");

    foreach (var field in new[] { "name", "operator", "value", "negate" }) {
      properties.GetProperty(field).GetProperty("description").GetString()
        .Should().NotBeNullOrWhiteSpace($"'{field}' must have a description");
    }

    properties.GetProperty("operator").GetProperty("enum").EnumerateArray().Select(v => v.GetString())
      .Should().Equal("equals", "notEquals", "greaterThan", "lessThan");
    properties.GetProperty("value").GetProperty("oneOf").EnumerateArray().Select(v => v.GetProperty("type").GetString())
      .Should().BeEquivalentTo("string", "integer");

    schema.GetProperty("description").GetString()
      .Should().Contain("whole number").And.Contain("case-insensitive").And.Contain("ordinal").And.Contain("multi-value");
  }
}
