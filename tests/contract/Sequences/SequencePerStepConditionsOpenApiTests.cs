using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequencePerStepConditionsOpenApiTests {
  private static readonly string[] RequiredCommandOutcomeFields = { "stepRef", "expectedState" };

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task SwaggerDocumentIncludesSequenceEndpointGroup() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var paths = document.RootElement.GetProperty("paths");

    paths.TryGetProperty("/api/sequences", out _).Should().BeTrue();
    paths.TryGetProperty("/api/sequences/{sequenceId}", out _).Should().BeTrue();
    paths.TryGetProperty("/api/sequences/{sequenceId}/execute", out _).Should().BeTrue();
  }

  [Fact]
  public async Task SwaggerDocumentDefinesPerStepConditionSchemasAndCommandOutcomeRequirements() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var root = document.RootElement;

    var schemas = root.GetProperty("components").GetProperty("schemas");

    schemas.TryGetProperty("SequenceStepCondition", out _).Should().BeTrue();
    schemas.TryGetProperty("ImageVisibleCondition", out _).Should().BeTrue();
    schemas.TryGetProperty("CommandOutcomeCondition", out _).Should().BeTrue();

    var commandOutcome = schemas.GetProperty("CommandOutcomeCondition");
    var properties = commandOutcome.GetProperty("properties");
    properties.TryGetProperty("stepRef", out _).Should().BeTrue();
    properties.TryGetProperty("expectedState", out _).Should().BeTrue();
    properties.GetProperty("stepRef").GetProperty("type").GetString().Should().Be("string");
    properties.GetProperty("expectedState").GetProperty("type").GetString().Should().Be("string");

    if (commandOutcome.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array) {
      var requiredFields = required.EnumerateArray().Select(entry => entry.GetString()).ToArray();
      requiredFields.Should().Contain(RequiredCommandOutcomeFields);
    }

    var sequenceStep = schemas.GetProperty("SequenceStep");
    var conditionProperty = sequenceStep.GetProperty("properties").GetProperty("condition");
    if (conditionProperty.TryGetProperty("$ref", out var conditionRef)) {
      conditionRef.GetString().Should().StartWith("#/components/schemas/SequenceStepCondition");
    }
    else {
      (conditionProperty.TryGetProperty("oneOf", out _) || conditionProperty.TryGetProperty("anyOf", out _)).Should().BeTrue();
    }

  }
}
