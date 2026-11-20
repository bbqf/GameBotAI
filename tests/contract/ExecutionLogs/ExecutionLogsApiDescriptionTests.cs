using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.ExecutionLogs;

/// <summary>
/// Feature 198 (B-044, FR-008): the OpenAPI document states the rules of a queue log.
/// </summary>
public sealed class ExecutionLogsApiDescriptionTests {
  private static async Task<JsonElement> ReadPathsAsync() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.GetProperty("paths").Clone();
  }

  [Fact]
  public async Task ListAndSubtreeDescriptionsStateTheQueueLogRules() {
    var paths = await ReadPathsAsync().ConfigureAwait(false);

    var list = paths.GetProperty("/api/execution-logs").GetProperty("get").GetProperty("description").GetString();
    var subtree = paths.GetProperty("/api/execution-logs/{id}/subtree").GetProperty("get").GetProperty("description").GetString();

    foreach (var description in new[] { list, subtree }) {
      description.Should().Contain("no limit on the number of children")
        .And.Contain("rotatedToExecutionId")
        .And.Contain("ended when the service stopped");
    }
    subtree.Should().Contain("cancellationReason");
  }
}
