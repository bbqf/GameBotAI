using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionLogsQueryIntegrationTests {
  [Fact]
  public async Task ListUsesDefaultPageSizeAndTimestampDescendingSort() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();

    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var service = app.Services.GetRequiredService<IExecutionLogService>();

      for (var i = 0; i < 55; i++) {
        await service.LogCommandExecutionAsync(
          $"cmd-default-{i:D3}",
          $"Default Command {i:D3}",
          "success",
          Array.Empty<PrimitiveTapStepOutcome>(),
          new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);
      }

      var response = await client.GetAsync(new Uri("/api/execution-logs", UriKind.Relative)).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();

      using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
      var items = doc.RootElement.GetProperty("items");
      items.GetArrayLength().Should().Be(50);
      doc.RootElement.TryGetProperty("nextPageToken", out var nextToken).Should().BeTrue();
      nextToken.ValueKind.Should().NotBe(JsonValueKind.Null);

      var firstTimestamp = items[0].GetProperty("timestampUtc").GetDateTimeOffset();
      var secondTimestamp = items[1].GetProperty("timestampUtc").GetDateTimeOffset();
      firstTimestamp.Should().BeOnOrAfter(secondTimestamp);
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }

  [Fact]
  public async Task ListAppliesCombinedSortAndFilterParameters() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();

    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var service = app.Services.GetRequiredService<IExecutionLogService>();

      await service.LogCommandExecutionAsync(
        "cmd-query-001",
        "alpha route",
        "failure",
        Array.Empty<PrimitiveTapStepOutcome>(),
        new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

      await service.LogCommandExecutionAsync(
        "cmd-query-002",
        "bravo route",
        "success",
        Array.Empty<PrimitiveTapStepOutcome>(),
        new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

      await service.LogCommandExecutionAsync(
        "cmd-query-003",
        "charlie route",
        "failure",
        Array.Empty<PrimitiveTapStepOutcome>(),
        new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

      var response = await client.GetAsync(new Uri("/api/execution-logs?sortBy=objectName&sortDirection=asc&filterStatus=fail&filterObjectName=route", UriKind.Relative)).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();

      using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
      var items = doc.RootElement.GetProperty("items");
      items.GetArrayLength().Should().Be(2);
      items[0].GetProperty("objectRef").GetProperty("displayNameSnapshot").GetString().Should().Be("alpha route");
      items[1].GetProperty("objectRef").GetProperty("displayNameSnapshot").GetString().Should().Be("charlie route");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
}
