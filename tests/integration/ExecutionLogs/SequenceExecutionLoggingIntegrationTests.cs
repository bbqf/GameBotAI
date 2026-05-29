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

  [Fact]
  public async Task SequenceExecutionDetailIncludesWaitForImageAttributes() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var logService = app.Services.GetRequiredService<IExecutionLogService>();

      await logService.LogSequenceExecutionAsync(
        "seq-wait-log",
        "Wait Log Sequence",
        "success",
        "Wait step completed by timeout.",
        new ExecutionLogContext { Depth = 0, SequenceId = "seq-wait-log", SequenceLabel = "Wait Log Sequence" },
        new[] {
          new ExecutionDetailItem(
            "step",
            "Step 'Wait for inbox' timeout_elapsed.",
            new Dictionary<string, object?> {
              ["stepOrder"] = 1,
              ["stepType"] = "waitForImage",
              ["status"] = "Succeeded",
              ["actionOutcome"] = "timeout_elapsed",
              ["reasonCode"] = "timeout_elapsed",
              ["timeoutMs"] = 1800,
              ["effectiveTimeoutMs"] = 1800,
              ["referenceImageId"] = "mail_icon",
              ["confidence"] = 0.87,
              ["exitCondition"] = "timeout_elapsed",
              ["imageLoadStatus"] = "loaded",
              ["sequenceId"] = "seq-wait-log",
              ["sequenceLabel"] = "Wait Log Sequence",
              ["stepId"] = "wait-step",
              ["stepLabel"] = "Wait for inbox"
            },
            "normal")
        }).ConfigureAwait(false);

      var listResp = await client.GetAsync(new Uri("/api/execution-logs?objectType=sequence&objectId=seq-wait-log&pageSize=1", UriKind.Relative)).ConfigureAwait(false);
      listResp.EnsureSuccessStatusCode();

      using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var id = listDoc.RootElement.GetProperty("items")[0].GetProperty("id").GetString();

      var detailResp = await client.GetAsync(new Uri($"/api/execution-logs/{id}", UriKind.Relative)).ConfigureAwait(false);
      detailResp.EnsureSuccessStatusCode();

      using var detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var step = detailDoc.RootElement.GetProperty("stepOutcomes")[0];

      step.GetProperty("stepType").GetString().Should().Be("waitForImage");
      step.GetProperty("stepName").GetString().Should().Be("Wait for inbox");
      step.GetProperty("status").GetString().Should().Be("timeout_elapsed");
      var detailAttributes = step.GetProperty("detailAttributes");
      detailAttributes.GetProperty("timeoutMs").GetInt32().Should().Be(1800);
      detailAttributes.GetProperty("effectiveTimeoutMs").GetInt32().Should().Be(1800);
      detailAttributes.GetProperty("referenceImageId").GetString().Should().Be("mail_icon");
      detailAttributes.GetProperty("confidence").GetDouble().Should().Be(0.87);
      detailAttributes.GetProperty("exitCondition").GetString().Should().Be("timeout_elapsed");
      detailAttributes.GetProperty("imageLoadStatus").GetString().Should().Be("loaded");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }

  [Fact]
  public async Task SequenceExecutionDetailIncludesStepLabelAndCommandNameForCommandBackedSteps() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var logService = app.Services.GetRequiredService<IExecutionLogService>();

      await logService.LogSequenceExecutionAsync(
        "seq-command-log",
        "Command Log Sequence",
        "success",
        "Sequence completed.",
        new ExecutionLogContext { Depth = 0, SequenceId = "seq-command-log", SequenceLabel = "Command Log Sequence" },
        new[] {
          new ExecutionDetailItem(
            "step",
            "Step 'Collect rewards' executed command 'Open Mail'.",
            new Dictionary<string, object?> {
              ["stepOrder"] = 1,
              ["stepType"] = "command",
              ["status"] = "Succeeded",
              ["actionOutcome"] = "executed",
              ["reasonCode"] = "executed",
              ["sequenceId"] = "seq-command-log",
              ["sequenceLabel"] = "Command Log Sequence",
              ["stepId"] = "step-collect",
              ["stepLabel"] = "Collect rewards",
              ["commandName"] = "Open Mail"
            },
            "normal")
        }).ConfigureAwait(false);

      var listResp = await client.GetAsync(new Uri("/api/execution-logs?objectType=sequence&objectId=seq-command-log&pageSize=1", UriKind.Relative)).ConfigureAwait(false);
      listResp.EnsureSuccessStatusCode();

      using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var id = listDoc.RootElement.GetProperty("items")[0].GetProperty("id").GetString();

      var detailResp = await client.GetAsync(new Uri($"/api/execution-logs/{id}", UriKind.Relative)).ConfigureAwait(false);
      detailResp.EnsureSuccessStatusCode();

      using var detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var step = detailDoc.RootElement.GetProperty("stepOutcomes")[0];

      step.GetProperty("stepName").GetString().Should().Be("Collect rewards");
      step.GetProperty("commandName").GetString().Should().Be("Open Mail");
      step.GetProperty("message").GetString().Should().Contain("Collect rewards");
      step.GetProperty("message").GetString().Should().Contain("Open Mail");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
}
