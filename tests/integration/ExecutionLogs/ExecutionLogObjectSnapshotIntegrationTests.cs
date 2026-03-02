using System.Collections.ObjectModel;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionLogObjectSnapshotIntegrationTests {
  [Fact]
  public async Task HistoricalLogKeepsOriginalSnapshotAfterCommandRenameAndDelete() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var commandRepository = app.Services.GetRequiredService<ICommandRepository>();
      var logService = app.Services.GetRequiredService<IExecutionLogService>();

      await commandRepository.AddAsync(new Command {
        Id = "cmd-snapshot-001",
        Name = "Original Snapshot Name",
        Steps = new Collection<CommandStep>()
      }).ConfigureAwait(false);

      await logService.LogCommandExecutionAsync(
        "cmd-snapshot-001",
        "Original Snapshot Name",
        "success",
        Array.Empty<PrimitiveTapStepOutcome>(),
        new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

      var command = await commandRepository.GetAsync("cmd-snapshot-001").ConfigureAwait(false);
      command.Should().NotBeNull();
      command!.Name = "Renamed Command";
      await commandRepository.UpdateAsync(command).ConfigureAwait(false);
      await commandRepository.DeleteAsync("cmd-snapshot-001").ConfigureAwait(false);

      var listResp = await client.GetAsync(new Uri("/api/execution-logs?objectType=command&objectId=cmd-snapshot-001&pageSize=1", UriKind.Relative)).ConfigureAwait(false);
      listResp.EnsureSuccessStatusCode();

      using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var item = listDoc.RootElement.GetProperty("items")[0];

      item.GetProperty("objectRef").GetProperty("objectId").GetString().Should().Be("cmd-snapshot-001");
      item.GetProperty("objectRef").GetProperty("displayNameSnapshot").GetString().Should().Be("Original Snapshot Name");
      item.GetProperty("navigation").GetProperty("directPath").GetString().Should().Be("/authoring/commands/cmd-snapshot-001");

      var deleted = await commandRepository.GetAsync("cmd-snapshot-001").ConfigureAwait(false);
      deleted.Should().BeNull();
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
}
