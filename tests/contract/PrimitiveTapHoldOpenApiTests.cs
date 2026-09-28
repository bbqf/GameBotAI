using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

/// <summary>
/// Feature 111 (issue #235): the OpenAPI document shows the hold duration of a PrimitiveTap step, with its range,
/// and the hold duration of the step outcome.
/// </summary>
public sealed class PrimitiveTapHoldOpenApiTests {
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
  public async Task PrimitiveTapConfigDocumentsTheHoldDurationAndItsRange() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);

    var config = schemas.GetProperty("PrimitiveTapConfigDto");
    var holdMs = config.GetProperty("properties").GetProperty("holdMs");

    holdMs.GetProperty("type").GetString().Should().Be("integer");
    holdMs.GetProperty("minimum").GetInt32().Should().Be(0);
    holdMs.GetProperty("maximum").GetInt32().Should().Be(5000);
    holdMs.GetProperty("description").GetString().Should().Contain("press and hold").And.Contain("single tap");
    config.GetProperty("required").EnumerateArray().Select(item => item.GetString())
      .Should().Contain("detectionTarget").And.NotContain("holdMs");
  }

  [Fact]
  public async Task StepOutcomeDocumentsTheHoldDuration() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);

    var holdMs = schemas.GetProperty("StepExecutionOutcomeDto").GetProperty("properties").GetProperty("holdMs");

    holdMs.GetProperty("type").GetString().Should().Be("integer");
    holdMs.GetProperty("description").GetString().Should().Contain("pressed and held");
  }
}
