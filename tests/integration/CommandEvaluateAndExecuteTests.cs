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

  public CommandEvaluateAndExecuteTests() {
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

  [Fact]
  public async Task EvaluateAndExecuteRunsWhenTriggerSatisfied() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Create a game
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "CmdExecGame", description = "desc" }).ConfigureAwait(true);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var gameId = game!["id"]!.ToString();

    // Create an action with a single tap step
    var actionReq = new {
      Name = "Action1",
      GameId = gameId,
      Steps = new[]
        {
                new { Type = "tap", Args = new Dictionary<string, object>{{"x", 5}, {"y", 5}}, DelayMs = (int?)null, DurationMs = (int?)null }
            }
    };
    var aResp = await client.PostAsJsonAsync(new Uri("/api/actions", UriKind.Relative), actionReq).ConfigureAwait(true);
    aResp.EnsureSuccessStatusCode();
    var act = await aResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var actionId = act!["id"]!.ToString();


    // Create a delay trigger with 0 seconds (immediately satisfied)
    var trigReq = new { Type = "delay", Enabled = true, CooldownSeconds = 0, Params = new { seconds = 0 } };
    var tResp = await client.PostAsJsonAsync(new Uri("/api/triggers", UriKind.Relative), trigReq).ConfigureAwait(true);
    tResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var tr = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var triggerId = tr!["id"]!.ToString();

    // Create a command that references the trigger and the action
    var cmdReq = new {
      Name = "C1",
      TriggerId = triggerId,
      Steps = new[] { new { Type = "Action", TargetId = actionId, Order = 1 } }
    };
    var cResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), cmdReq).ConfigureAwait(true);
    cResp.EnsureSuccessStatusCode();
    var cmd = await cResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var commandId = cmd!["id"]!.ToString();

    // Create a session (stub mode, no adb)
    var sResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(true);
    sResp.EnsureSuccessStatusCode();
    var s = await sResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var sessionId = s!["id"]!.ToString();

    // Evaluate-and-execute should run since trigger is satisfied
    var exec = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec.StatusCode.Should().Be(HttpStatusCode.Accepted);

    var execPayload = await exec.Content.ReadAsStringAsync().ConfigureAwait(true);
    int accepted;
    string? triggerStatus;
    string? message;
    using (var doc = JsonDocument.Parse(execPayload)) {
      var root = doc.RootElement;
      accepted = root.GetProperty("accepted").GetInt32();
      triggerStatus = root.GetProperty("triggerStatus").GetString();
      message = root.GetProperty("message").GetString();
    }
    accepted.Should().BeGreaterThan(0);
    triggerStatus.Should().Be("Satisfied");
    message.Should().Be("delay_elapsed");

    // Trigger state fields are no longer returned on GET; acceptance and status assertions above are sufficient.
  }

  [Fact]
  public async Task EvaluateAndExecuteDoesNotRunWhenTriggerPending() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "CmdExecGame", description = "desc" }).ConfigureAwait(true);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var gameId = game!["id"]!.ToString();

    var actionReq = new {
      Name = "Action1",
      GameId = gameId,
      Steps = new[]
        {
                new { Type = "tap", Args = new Dictionary<string, object>{{"x", 5}, {"y", 5}}, DelayMs = (int?)null, DurationMs = (int?)null }
            }
    };
    var aResp = await client.PostAsJsonAsync(new Uri("/api/actions", UriKind.Relative), actionReq).ConfigureAwait(true);
    aResp.EnsureSuccessStatusCode();
    var act = await aResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var actionId = act!["id"]!.ToString();


    // Delay trigger pending (seconds > 0)
    var trigReq = new { Type = "delay", Enabled = true, CooldownSeconds = 0, Params = new { seconds = 5 } };
    var tResp = await client.PostAsJsonAsync(new Uri("/api/triggers", UriKind.Relative), trigReq).ConfigureAwait(true);
    tResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var tr = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var triggerId = tr!["id"]!.ToString();

    var cmdReq = new {
      Name = "C1",
      TriggerId = triggerId,
      Steps = new[] { new { Type = "Action", TargetId = actionId, Order = 1 } }
    };
    var cResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), cmdReq).ConfigureAwait(true);
    cResp.EnsureSuccessStatusCode();
    var cmd = await cResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var commandId = cmd!["id"]!.ToString();

    var sResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(true);
    sResp.EnsureSuccessStatusCode();
    var s = await sResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var sessionId = s!["id"]!.ToString();

    var exec = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec.StatusCode.Should().Be(HttpStatusCode.Accepted);

    var execPayload = await exec.Content.ReadAsStringAsync().ConfigureAwait(true);
    int accepted;
    string? triggerStatus;
    string? message;
    using (var doc = JsonDocument.Parse(execPayload)) {
      var root = doc.RootElement;
      accepted = root.GetProperty("accepted").GetInt32();
      triggerStatus = root.GetProperty("triggerStatus").GetString();
      message = root.GetProperty("message").GetString();
    }
    accepted.Should().Be(0);
    triggerStatus.Should().Be("Pending");
    message.Should().Be("delay_pending_initial");

    // Trigger state fields are no longer returned on GET; assertion on Accepted/Pending above is sufficient.
  }

  [Fact]
  public async Task EvaluateAndExecuteRespectsCooldownDoesNotRunSecondTime() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "CmdExecGame", description = "desc" }).ConfigureAwait(true);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var gameId = game!["id"]!.ToString();

    var actionReq = new {
      Name = "Action1",
      GameId = gameId,
      Steps = new[]
        {
                new { Type = "tap", Args = new Dictionary<string, object>{{"x", 5}, {"y", 5}}, DelayMs = (int?)null, DurationMs = (int?)null }
            }
    };
    var aResp = await client.PostAsJsonAsync(new Uri("/api/actions", UriKind.Relative), actionReq).ConfigureAwait(true);
    aResp.EnsureSuccessStatusCode();
    var act = await aResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var actionId = act!["id"]!.ToString();


    // Immediate satisfaction but with cooldown
    var trigReq = new { type = "delay", enabled = true, cooldownSeconds = 60, @params = new { seconds = 0 } };
    var tResp = await client.PostAsJsonAsync(new Uri("/api/triggers", UriKind.Relative), trigReq).ConfigureAwait(true);
    tResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var tr = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var triggerId = tr!["id"]!.ToString();

    var cmdReq = new {
      name = "C1",
      triggerId,
      steps = new[] { new { type = "Action", targetId = actionId, order = 1 } }
    };
    var cResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), cmdReq).ConfigureAwait(true);
    cResp.EnsureSuccessStatusCode();
    var cmd = await cResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var commandId = cmd!["id"]!.ToString();

    var sResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(true);
    sResp.EnsureSuccessStatusCode();
    var s = await sResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var sessionId = s!["id"]!.ToString();

    // First execution should run
    var exec1 = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec1.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var execPayload1 = await exec1.Content.ReadAsStringAsync().ConfigureAwait(true);
    int accepted1;
    string? triggerStatus1;
    using (var doc = JsonDocument.Parse(execPayload1)) {
      var root = doc.RootElement;
      accepted1 = root.GetProperty("accepted").GetInt32();
      triggerStatus1 = root.GetProperty("triggerStatus").GetString();
    }
    accepted1.Should().BeGreaterThan(0);
    triggerStatus1.Should().Be("Satisfied");

    // Trigger GET no longer returns lastFiredAt; rely on decision.Accepted/Status.

    // Second execution should be suppressed by cooldown
    var exec2 = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec2.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var execPayload2 = await exec2.Content.ReadAsStringAsync().ConfigureAwait(true);
    int accepted2;
    string? triggerStatus2;
    string? message2;
    using (var doc = JsonDocument.Parse(execPayload2)) {
      var root = doc.RootElement;
      accepted2 = root.GetProperty("accepted").GetInt32();
      triggerStatus2 = root.GetProperty("triggerStatus").GetString();
      message2 = root.GetProperty("message").GetString();
    }
    accepted2.Should().Be(0);
    triggerStatus2.Should().Be("Cooldown");
    message2.Should().Be("cooldown_active");

    // Cooldown verified via response payload; no additional trigger state checks.
  }

  [Fact]
  public async Task EvaluateAndExecuteDoesNotRunWhenTriggerDisabled() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameResp = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "CmdExecGame", description = "desc" }).ConfigureAwait(true);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var gameId = game!["id"]!.ToString();

    var actionReq = new {
      name = "Action1",
      gameId,
      steps = new[]
        {
                new { type = "tap", args = new Dictionary<string, object>{{"x", 5}, {"y", 5}}, delayMs = (int?)null, durationMs = (int?)null }
            }
    };
    var aResp = await client.PostAsJsonAsync(new Uri("/actions", UriKind.Relative), actionReq).ConfigureAwait(true);
    aResp.EnsureSuccessStatusCode();
    var act = await aResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var actionId = act!["id"]!.ToString();


    // Disabled trigger should not fire even if otherwise satisfied
    var trigReq = new { Type = "delay", Enabled = false, CooldownSeconds = 0, Params = new { seconds = 0 } };
    var tResp = await client.PostAsJsonAsync(new Uri("/api/triggers", UriKind.Relative), trigReq).ConfigureAwait(true);
    tResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var tr = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var triggerId = tr!["id"]!.ToString();

    var cmdReq = new {
      Name = "C1",
      TriggerId = triggerId,
      Steps = new[] { new { Type = "Action", TargetId = actionId, Order = 1 } }
    };
    var cResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), cmdReq).ConfigureAwait(true);
    cResp.EnsureSuccessStatusCode();
    var cmd = await cResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var commandId = cmd!["id"]!.ToString();

    var sResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(true);
    sResp.EnsureSuccessStatusCode();
    var s = await sResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var sessionId = s!["id"]!.ToString();

    var exec = await client.PostAsync(new Uri($"/api/commands/{commandId}/evaluate-and-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    exec.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var execPayload = await exec.Content.ReadAsStringAsync().ConfigureAwait(true);
    int accepted;
    string? triggerStatus;
    string? message;
    using (var doc = JsonDocument.Parse(execPayload)) {
      var root = doc.RootElement;
      accepted = root.GetProperty("accepted").GetInt32();
      triggerStatus = root.GetProperty("triggerStatus").GetString();
      message = root.GetProperty("message").GetString();
    }
    accepted.Should().Be(0);
    triggerStatus.Should().Be("Disabled");
    message.Should().Be("trigger_disabled");
  }
}
