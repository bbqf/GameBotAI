using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class CommandExecutionLoggingIntegrationTests {
  [Fact]
  public async Task CommandExecutionIsPersistedAndQueryableViaExecutionLogsEndpoint() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var logService = app.Services.GetRequiredService<IExecutionLogService>();

      await logService.LogCommandExecutionAsync(
        "cmd-us1-persist",
        "US1 Persisted Command",
        "success",
        new[] { new PrimitiveTapStepOutcome(1, "executed", null, new PrimitiveTapResolvedPoint(11, 22), 0.95) },
        new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

      var listResp = await client.GetAsync(new Uri("/api/execution-logs?objectType=command&objectId=cmd-us1-persist&pageSize=1", UriKind.Relative)).ConfigureAwait(false);
      listResp.EnsureSuccessStatusCode();

      using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var items = listDoc.RootElement.GetProperty("items");
      items.GetArrayLength().Should().Be(1);

      var item = items[0];
      item.GetProperty("executionType").GetString().Should().Be("command");
      item.GetProperty("finalStatus").GetString().Should().Be("success");
      item.GetProperty("objectRef").GetProperty("objectId").GetString().Should().Be("cmd-us1-persist");

      var id = item.GetProperty("id").GetString();
      var detailResp = await client.GetAsync(new Uri($"/api/execution-logs/{id}", UriKind.Relative)).ConfigureAwait(false);
      detailResp.EnsureSuccessStatusCode();

      using var detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      detailDoc.RootElement.GetProperty("stepOutcomes").GetArrayLength().Should().Be(1);
      detailDoc.RootElement.GetProperty("navigation").GetProperty("directPath").GetString().Should().Be("/authoring/commands/cmd-us1-persist");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
}
