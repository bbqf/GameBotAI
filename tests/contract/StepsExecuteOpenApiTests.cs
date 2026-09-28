using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

/// <summary>
/// Feature 112 (issue #222): the OpenAPI document of POST /api/steps/execute states the outcome contract
/// and the execution-log entry.
/// </summary>
public sealed class StepsExecuteOpenApiTests {
  [Fact]
  public async Task SwaggerStepsExecuteDescribesTheOutcomeContract() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

    var description = document.RootElement
      .GetProperty("paths").GetProperty("/api/steps/execute").GetProperty("post")
      .GetProperty("description").GetString();

    description.Should().Contain("no input").And.Contain("execution-log").And.Contain("dispatch_unknown");
  }
}
