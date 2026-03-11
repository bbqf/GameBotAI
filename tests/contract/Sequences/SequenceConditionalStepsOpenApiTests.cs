using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequenceConditionalStepsOpenApiTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task SwaggerDocumentExposesConditionalStepSchemaWithStepType() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

    schemas.TryGetProperty("FlowStepDto", out var flowStepSchema).Should().BeTrue();
    flowStepSchema.GetProperty("properties").TryGetProperty("stepType", out _).Should().BeTrue();
    flowStepSchema.GetProperty("properties").TryGetProperty("condition", out _).Should().BeTrue();
    flowStepSchema.GetProperty("properties").TryGetProperty("payloadRef", out _).Should().BeTrue();

    flowStepSchema.TryGetProperty("required", out var required).Should().BeTrue();
    required.GetArrayLength().Should().BeGreaterThan(0);
    required.EnumerateArray().Any(item => string.Equals(item.GetString(), "stepType", StringComparison.Ordinal)).Should().BeTrue();

    schemas.TryGetProperty("ConditionExpression", out _).Should().BeTrue();
    schemas.TryGetProperty("ConditionOperand", out _).Should().BeTrue();
    schemas.TryGetProperty("ConditionEvaluationTrace", out _).Should().BeTrue();
    schemas.TryGetProperty("SequenceFlowUpsertRequest", out _).Should().BeTrue();
  }
}
