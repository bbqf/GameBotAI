using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

/// <summary>
/// Feature 112 (issue #222): each POST /api/steps/execute call that passes the session check writes one
/// execution-log entry of the type step, also for a timeout. GET /api/execution-logs returns it with the
/// time filter.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class StepExecutionLogIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public StepExecutionLogIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    GC.SuppressFinalize(this);
  }

  private static HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateSessionAsync(HttpClient client) {
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "StepLogGame", description = "desc" }).ConfigureAwait(false);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = game!["id"]!.ToString() }).ConfigureAwait(false);
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    return session!["id"]!.ToString()!;
  }

  private static async Task<JsonElement> ReadStepEntriesAsync(HttpClient client, DateTimeOffset fromUtc, DateTimeOffset toUtc, string? sessionId) {
    var from = Uri.EscapeDataString(fromUtc.ToString("O", CultureInfo.InvariantCulture));
    var to = Uri.EscapeDataString(toUtc.ToString("O", CultureInfo.InvariantCulture));
    var uri = $"/api/execution-logs?fromUtc={from}&toUtc={to}&objectType=step";
    if (sessionId is not null) uri += $"&objectId={Uri.EscapeDataString(sessionId)}";
    var response = await client.GetAsync(new Uri(uri, UriKind.Relative)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    using var doc = JsonDocument.Parse(body);
    return doc.RootElement.GetProperty("items").Clone();
  }

  [Fact]
  public async Task ExecuteStepWritesAnExecutionLogEntryVisibleWithTheTimeFilter() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var sessionId = await CreateSessionAsync(client).ConfigureAwait(false);
    var before = DateTimeOffset.UtcNow.AddSeconds(-1);

    var response = await client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), new {
      sessionId,
      step = new { type = "KeyInput", order = 0, keyInput = new { key = "HOME" } }
    }).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync().ConfigureAwait(false));

    var items = await ReadStepEntriesAsync(client, before, DateTimeOffset.UtcNow.AddSeconds(1), sessionId).ConfigureAwait(false);
    items.GetArrayLength().Should().Be(1);
    var entry = items[0];
    entry.GetProperty("executionType").GetString().Should().Be("step");
    entry.GetProperty("objectRef").GetProperty("objectType").GetString().Should().Be("step");
    entry.GetProperty("objectRef").GetProperty("objectId").GetString().Should().Be(sessionId);
    entry.GetProperty("finalStatus").GetString().Should().Be("success");

    var stepOutcome = entry.GetProperty("stepOutcomes")[0];
    stepOutcome.GetProperty("stepType").GetString().Should().Be("keyInput");
    stepOutcome.GetProperty("outcome").GetString().Should().Be("executed");
    stepOutcome.GetProperty("reasonCode").GetString().Should().Be("executed");

    var attributes = entry.GetProperty("details")[0].GetProperty("attributes");
    attributes.GetProperty("sessionId").GetString().Should().Be(sessionId);
    attributes.GetProperty("stepType").GetString().Should().Be("KeyInput");
    attributes.GetProperty("status").GetString().Should().Be("executed");
    attributes.GetProperty("accepted").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    attributes.TryGetProperty("startedAtUtc", out _).Should().BeTrue();
    attributes.TryGetProperty("durationMs", out _).Should().BeTrue();
    attributes.TryGetProperty("resolvedX", out _).Should().BeTrue();
    attributes.TryGetProperty("executedX", out _).Should().BeTrue();
  }

  [Fact]
  public async Task ExecuteStepTimeoutWritesAnExecutionLogEntry() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var sessionId = await CreateSessionAsync(client).ConfigureAwait(false);
    var before = DateTimeOffset.UtcNow.AddSeconds(-1);

    // A wait with no image is a plain delay. 12 seconds is more than the 10-second limit of the route.
    var response = await client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), new {
      sessionId,
      step = new { type = "WaitForImage", order = 0, waitForImage = new { timeoutMs = 12000 } }
    }).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    using (var doc = JsonDocument.Parse(body)) {
      doc.RootElement.GetProperty("stepOutcomes")[0].GetProperty("status").GetString().Should().Be("timeout");
    }

    var items = await ReadStepEntriesAsync(client, before, DateTimeOffset.UtcNow.AddSeconds(1), sessionId).ConfigureAwait(false);
    items.GetArrayLength().Should().Be(1);
    var entry = items[0];
    entry.GetProperty("finalStatus").GetString().Should().Be("failure");
    var stepOutcome = entry.GetProperty("stepOutcomes")[0];
    stepOutcome.GetProperty("stepType").GetString().Should().Be("waitForImage");
    stepOutcome.GetProperty("outcome").GetString().Should().Be("timeout");
    entry.GetProperty("details")[0].GetProperty("attributes").GetProperty("accepted").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task ExecuteStepWithoutSessionWritesNoEntry() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var before = DateTimeOffset.UtcNow.AddSeconds(-1);

    var response = await client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), new {
      step = new { type = "KeyInput", order = 0, keyInput = new { key = "HOME" } }
    }).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var items = await ReadStepEntriesAsync(client, before, DateTimeOffset.UtcNow.AddSeconds(1), null).ConfigureAwait(false);
    items.GetArrayLength().Should().Be(0);
  }
}
