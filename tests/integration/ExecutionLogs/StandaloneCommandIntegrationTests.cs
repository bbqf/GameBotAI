using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class StandaloneCommandIntegrationTests {
  [Fact]
  public async Task DirectCommandExecutionIsTopLevelLeafWithoutChildren() {
    TestEnvironment.PrepareCleanDataDir();

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var svc = app.Services.GetRequiredService<IExecutionLogService>();
    // A directly executed command logs with the default (root) context — no parent.
    await svc.LogCommandExecutionAsync(
      "cmd-solo", "Solo Command", "success",
      new[] { new PrimitiveTapStepOutcome(1, "executed", null, new PrimitiveTapResolvedPoint(5, 5), 0.95) },
      new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

    var response = await client.GetAsync(new Uri("/api/execution-logs?filterObjectName=Solo%20Command&pageSize=50", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var items = doc.RootElement.GetProperty("items");
    items.GetArrayLength().Should().Be(1);

    var item = items[0];
    item.GetProperty("executionType").GetString().Should().Be("command");
    item.GetProperty("childCount").GetInt32().Should().Be(0, "a stand-alone command is a leaf, not expandable");
    item.GetProperty("finalStatus").GetString().Should().Be("success");
    var hierarchy = item.GetProperty("hierarchy");
    (hierarchy.TryGetProperty("parentExecutionId", out var parent) && parent.ValueKind == JsonValueKind.String)
      .Should().BeFalse("a directly executed command is top-level (no parent)");
  }
}
