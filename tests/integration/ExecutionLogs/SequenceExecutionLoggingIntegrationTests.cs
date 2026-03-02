using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class SequenceExecutionLoggingIntegrationTests {
  [Fact]
  public async Task SequenceExecutionIsPersistedAndQueryableViaExecutionLogsEndpoint() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var logService = app.Services.GetRequiredService<IExecutionLogService>();

      await logService.LogSequenceExecutionAsync(
        "seq-us1-persist",
        "US1 Persisted Sequence",
        "failure",
        "Sequence failed while evaluating a step.",
        new ExecutionLogContext { Depth = 0 },
        new[] {
          new ExecutionDetailItem(
            "sequence",
            "Executed commands: cmd-a,cmd-b",
            new Dictionary<string, object?> { ["executedCount"] = 2 },
            "normal")
        }).ConfigureAwait(false);

      var listResp = await client.GetAsync(new Uri("/api/execution-logs?objectType=sequence&objectId=seq-us1-persist&pageSize=1", UriKind.Relative)).ConfigureAwait(false);
      listResp.EnsureSuccessStatusCode();

      using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var items = listDoc.RootElement.GetProperty("items");
      items.GetArrayLength().Should().Be(1);

      var item = items[0];
      item.GetProperty("executionType").GetString().Should().Be("sequence");
      item.GetProperty("finalStatus").GetString().Should().Be("failure");
      item.GetProperty("objectRef").GetProperty("displayNameSnapshot").GetString().Should().Be("US1 Persisted Sequence");
      item.GetProperty("navigation").GetProperty("directPath").GetString().Should().Be("/authoring/sequences/seq-us1-persist");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
}
