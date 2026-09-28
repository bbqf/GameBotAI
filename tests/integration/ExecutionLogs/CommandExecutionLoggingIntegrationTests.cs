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

  [Fact]
  public async Task CommandExecutionDetailIncludesWaitForImageAttributes() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var logService = app.Services.GetRequiredService<IExecutionLogService>();

      await logService.LogCommandExecutionAsync(
        "cmd-wait-log",
        "Wait Log Command",
        "success",
        new[] {
          new PrimitiveTapStepOutcome(
            1,
            "completed_timeout",
            "timeout_elapsed",
            null,
            null,
            StepType: "waitForImage",
            TimeoutMs: 1500,
            EffectiveTimeoutMs: 1500,
            ReferenceImageId: "mail_icon",
            ImageLoadStatus: "loaded")
        },
        new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

      var listResp = await client.GetAsync(new Uri("/api/execution-logs?objectType=command&objectId=cmd-wait-log&pageSize=1", UriKind.Relative)).ConfigureAwait(false);
      listResp.EnsureSuccessStatusCode();

      using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var id = listDoc.RootElement.GetProperty("items")[0].GetProperty("id").GetString();

      var detailResp = await client.GetAsync(new Uri($"/api/execution-logs/{id}", UriKind.Relative)).ConfigureAwait(false);
      detailResp.EnsureSuccessStatusCode();

      using var detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var step = detailDoc.RootElement.GetProperty("stepOutcomes")[0];

      step.GetProperty("stepName").GetString().Should().Be("waitForImage");
      step.GetProperty("status").GetString().Should().Be("completed_timeout");
      step.GetProperty("message").GetString().Should().Be("timeout_elapsed");
      var detailAttributes = step.GetProperty("detailAttributes");
      detailAttributes.GetProperty("timeoutMs").GetInt32().Should().Be(1500);
      detailAttributes.GetProperty("effectiveTimeoutMs").GetInt32().Should().Be(1500);
      detailAttributes.GetProperty("referenceImageId").GetString().Should().Be("mail_icon");
      detailAttributes.GetProperty("exitCondition").GetString().Should().Be("timeout_elapsed");
      detailAttributes.GetProperty("imageLoadStatus").GetString().Should().Be("loaded");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
  // Feature 111 (issue #235): the tap detail of a press and hold shows the point and the hold duration.
  [Fact]
  public async Task PressAndHoldDetailShowsTheTargetTheExecutedPointAndTheHoldDuration() {
    var tap = await LogOneTapAndReadDetailAsync("cmd-hold-moved",
      new PrimitiveTapStepOutcome(0, "executed", null, new PrimitiveTapResolvedPoint(11, 22), 0.95,
        ExecutedPoint: new PrimitiveTapResolvedPoint(13, 20), HoldMs: 700)).ConfigureAwait(false);

    tap.GetProperty("message").GetString().Should().Be("Press and hold targeted (11,22), executed at (13,20) for 700 ms.");
    var attributes = tap.GetProperty("attributes");
    attributes.GetProperty("holdMs").GetInt32().Should().Be(700);
    attributes.GetProperty("x").GetInt32().Should().Be(11);
    attributes.GetProperty("y").GetInt32().Should().Be(22);
    attributes.GetProperty("executedX").GetInt32().Should().Be(13);
    attributes.GetProperty("executedY").GetInt32().Should().Be(20);
  }

  [Fact]
  public async Task PressAndHoldDetailAtTheTargetPointShowsThePointAndTheHoldDuration() {
    var tap = await LogOneTapAndReadDetailAsync("cmd-hold-same",
      new PrimitiveTapStepOutcome(0, "executed", null, new PrimitiveTapResolvedPoint(11, 22), 0.95,
        ExecutedPoint: new PrimitiveTapResolvedPoint(11, 22), HoldMs: 700)).ConfigureAwait(false);

    tap.GetProperty("message").GetString().Should().Be("Press and hold at (11,22) for 700 ms.");
    tap.GetProperty("attributes").GetProperty("holdMs").GetInt32().Should().Be(700);
  }

  [Fact]
  public async Task TapDetailWithoutHoldDurationDoesNotChange() {
    var tap = await LogOneTapAndReadDetailAsync("cmd-tap-plain",
      new PrimitiveTapStepOutcome(0, "executed", null, new PrimitiveTapResolvedPoint(11, 22), 0.95,
        ExecutedPoint: new PrimitiveTapResolvedPoint(11, 22))).ConfigureAwait(false);

    tap.GetProperty("message").GetString().Should().Be("Tap executed at (11,22).");
    tap.GetProperty("attributes").TryGetProperty("holdMs", out _).Should().BeFalse();
  }

  private static async Task<JsonElement> LogOneTapAndReadDetailAsync(string commandId, PrimitiveTapStepOutcome outcome) {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var logService = app.Services.GetRequiredService<IExecutionLogService>();

      await logService.LogCommandExecutionAsync(commandId, "Hold Command", "success", new[] { outcome },
        new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

      var listResp = await client.GetAsync(new Uri($"/api/execution-logs?objectType=command&objectId={commandId}&pageSize=1", UriKind.Relative)).ConfigureAwait(false);
      listResp.EnsureSuccessStatusCode();
      using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var id = listDoc.RootElement.GetProperty("items")[0].GetProperty("id").GetString();

      var detailResp = await client.GetAsync(new Uri($"/api/execution-logs/{id}", UriKind.Relative)).ConfigureAwait(false);
      detailResp.EnsureSuccessStatusCode();
      using var detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync().ConfigureAwait(false));
      var tap = detailDoc.RootElement.GetProperty("details").EnumerateArray()
        .First(item => item.GetProperty("kind").GetString() == "tap");
      return tap.Clone();
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }
}
