using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class CommandEvaluateAndExecuteTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string? _prevTestScreenImage;

  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public CommandEvaluateAndExecuteTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevTestScreenImage = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", _prevTestScreenImage);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task EvaluateAndExecuteRunsWhenTriggerSatisfied() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameId = await CreateGameAsync(client).ConfigureAwait(true);
    await UploadTemplateAsync(client).ConfigureAwait(true);
    var triggerId = await CreateDelayTriggerAsync(client, enabled: true, cooldownSeconds: 0, seconds: 0).ConfigureAwait(true);
    var commandId = await CreatePrimitiveCommandAsync(client, triggerId).ConfigureAwait(true);
    var sessionId = await CreateSessionAsync(client, gameId).ConfigureAwait(true);

    var exec = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = JsonDocument.Parse(await exec.Content.ReadAsStringAsync().ConfigureAwait(true));
    doc.RootElement.GetProperty("accepted").GetInt32().Should().BeGreaterThan(0);
    doc.RootElement.GetProperty("triggerStatus").GetString().Should().Be("Satisfied");
    doc.RootElement.GetProperty("message").GetString().Should().Be("delay_elapsed");
  }

  [Fact]
  public async Task EvaluateAndExecuteDoesNotRunWhenTriggerPending() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameId = await CreateGameAsync(client).ConfigureAwait(true);
    await UploadTemplateAsync(client).ConfigureAwait(true);
    var triggerId = await CreateDelayTriggerAsync(client, enabled: true, cooldownSeconds: 0, seconds: 5).ConfigureAwait(true);
    var commandId = await CreatePrimitiveCommandAsync(client, triggerId).ConfigureAwait(true);
    var sessionId = await CreateSessionAsync(client, gameId).ConfigureAwait(true);

    var exec = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = JsonDocument.Parse(await exec.Content.ReadAsStringAsync().ConfigureAwait(true));
    doc.RootElement.GetProperty("accepted").GetInt32().Should().Be(0);
    doc.RootElement.GetProperty("triggerStatus").GetString().Should().Be("Pending");
    doc.RootElement.GetProperty("message").GetString().Should().Be("delay_pending_initial");
  }

  [Fact]
  public async Task EvaluateAndExecuteRespectsCooldownDoesNotRunSecondTime() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameId = await CreateGameAsync(client).ConfigureAwait(true);
    await UploadTemplateAsync(client).ConfigureAwait(true);
    var triggerId = await CreateDelayTriggerAsync(client, enabled: true, cooldownSeconds: 60, seconds: 0).ConfigureAwait(true);
    var commandId = await CreatePrimitiveCommandAsync(client, triggerId).ConfigureAwait(true);
    var sessionId = await CreateSessionAsync(client, gameId).ConfigureAwait(true);

    var exec1 = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec1.StatusCode.Should().Be(HttpStatusCode.Accepted);
    using var doc1 = JsonDocument.Parse(await exec1.Content.ReadAsStringAsync().ConfigureAwait(true));
    doc1.RootElement.GetProperty("accepted").GetInt32().Should().BeGreaterThan(0);
    doc1.RootElement.GetProperty("triggerStatus").GetString().Should().Be("Satisfied");

    var exec2 = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec2.StatusCode.Should().Be(HttpStatusCode.Accepted);
    using var doc2 = JsonDocument.Parse(await exec2.Content.ReadAsStringAsync().ConfigureAwait(true));
    doc2.RootElement.GetProperty("accepted").GetInt32().Should().Be(0);
    doc2.RootElement.GetProperty("triggerStatus").GetString().Should().Be("Cooldown");
    doc2.RootElement.GetProperty("message").GetString().Should().Be("cooldown_active");
  }

  [Fact]
  public async Task EvaluateAndExecuteDoesNotRunWhenTriggerDisabled() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameId = await CreateGameAsync(client).ConfigureAwait(true);
    await UploadTemplateAsync(client).ConfigureAwait(true);
    var triggerId = await CreateDelayTriggerAsync(client, enabled: false, cooldownSeconds: 0, seconds: 0).ConfigureAwait(true);
    var commandId = await CreatePrimitiveCommandAsync(client, triggerId).ConfigureAwait(true);
    var sessionId = await CreateSessionAsync(client, gameId).ConfigureAwait(true);

    var exec = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec.StatusCode.Should().Be(HttpStatusCode.Accepted);
    using var doc = JsonDocument.Parse(await exec.Content.ReadAsStringAsync().ConfigureAwait(true));
    doc.RootElement.GetProperty("accepted").GetInt32().Should().Be(0);
    doc.RootElement.GetProperty("triggerStatus").GetString().Should().Be("Disabled");
    doc.RootElement.GetProperty("message").GetString().Should().Be("trigger_disabled");
  }

  private static async Task<string> CreateGameAsync(HttpClient client) {
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "CmdExecGame", description = "desc" }).ConfigureAwait(true);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    return game!["id"]!.ToString()!;
  }

  private static async Task UploadTemplateAsync(HttpClient client) {
    var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "home_button", data = OneByOnePngBase64 }).ConfigureAwait(true);
    uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  private static async Task<string> CreateDelayTriggerAsync(HttpClient client, bool enabled, int cooldownSeconds, int seconds) {
    var trigReq = new { Type = "delay", Enabled = enabled, CooldownSeconds = cooldownSeconds, Params = new { seconds } };
    var trigResp = await client.PostAsJsonAsync(new Uri("/api/triggers", UriKind.Relative), trigReq).ConfigureAwait(true);
    trigResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var trigger = await trigResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    return trigger!["id"]!.ToString()!;
  }

  private static async Task<string> CreatePrimitiveCommandAsync(HttpClient client, string triggerId) {
    var cmdReq = new {
      Name = "C1",
      TriggerId = triggerId,
      Steps = new[] {
        new {
          Type = "PrimitiveTap",
          Order = 1,
          PrimitiveTap = new {
            DetectionTarget = new {
              ReferenceImageId = "home_button",
              Confidence = 0.99,
              OffsetX = 0,
              OffsetY = 0,
              SelectionStrategy = "HighestConfidence"
            }
          }
        }
      }
    };

    var cmdResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), cmdReq).ConfigureAwait(true);
    cmdResp.EnsureSuccessStatusCode();
    var command = await cmdResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    return command!["id"]!.ToString()!;
  }

  private static async Task<string> CreateSessionAsync(HttpClient client, string gameId) {
    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(true);
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    return session!["id"]!.ToString()!;
  }
}
