using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionLogsDetailIntegrationTests {
  [Fact]
  public async Task DetailMapsRelatedObjectLinksAndSnapshotAvailability() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();

    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

      var repository = app.Services.GetRequiredService<IExecutionLogRepository>();
      var entry = new ExecutionLogEntry {
        Id = "exe-detail-map-001",
        TimestampUtc = DateTimeOffset.UtcNow,
        ExecutionType = "command",
        FinalStatus = "success",
        ObjectRef = new ExecutionObjectReference("command", "cmd-001", "Farm Command"),
        Navigation = new ExecutionNavigationContext("/authoring/commands/cmd-001", "/execution/parent"),
        Hierarchy = new ExecutionHierarchyContext("exe-root-001", null, 1, 0),
        Summary = "Command completed.",
        Details = new[]
        {
          new ExecutionDetailItem(
            "snapshot",
            "Captured screen",
            new Dictionary<string, object?> { ["imageUrl"] = "/api/images/snapshot-1" },
            "normal")
        },
        StepOutcomes = new[]
        {
          new ExecutionStepOutcome(1, "tap", "executed", "ok", "Tapped target")
        },
        RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
      };

      await repository.AddAsync(entry).ConfigureAwait(false);

      var response = await client.GetAsync(new Uri($"/api/execution-logs/{entry.Id}", UriKind.Relative)).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();

      using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
      var root = doc.RootElement;

      root.GetProperty("executionId").GetString().Should().Be(entry.Id);
      var related = root.GetProperty("relatedObjects");
      related.GetArrayLength().Should().Be(2);
      related[0].GetProperty("label").GetString().Should().Be("Farm Command");
      related[1].GetProperty("isAvailable").GetBoolean().Should().BeFalse();
      related[1].GetProperty("unavailableReason").GetString().Should().Be("Parent execution is unavailable.");

      var snapshot = root.GetProperty("snapshot");
      snapshot.GetProperty("isAvailable").GetBoolean().Should().BeTrue();
      snapshot.GetProperty("caption").GetString().Should().Be("Snapshot captured during execution.");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
}
