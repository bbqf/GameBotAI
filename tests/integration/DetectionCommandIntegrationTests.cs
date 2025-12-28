using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class DetectionCommandIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  // 1x1 PNG white; acts as both screenshot and template to force a single unique match
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public DetectionCommandIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

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
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", null);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task ForceExecuteCommandWithDetectionResolvesTapCoordinates() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Persist reference image used by detection
    var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "home_button", data = OneByOnePngBase64 });
    uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

    // Create game
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "DetectCmdGame", description = "desc" });
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var gameId = game!["id"]!.ToString();

    // Create action with a single tap; initial x/y will be overridden by adapter
    var actionReq = new {
      Name = "TapByDetect",
      GameId = gameId,
      Steps = new[] { new { Type = "tap", Args = new Dictionary<string, object>{{"x", 1}, {"y", 1}}, DelayMs = (int?)null, DurationMs = (int?)null } }
    };
    var aResp = await client.PostAsJsonAsync(new Uri("/api/actions", UriKind.Relative), actionReq);
    aResp.EnsureSuccessStatusCode();
    var act = await aResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var actionId = act!["id"]!.ToString();

    // Create command with detection referencing the persisted image
    var cmdReq = new {
      Name = "DetectAndTap",
      TriggerId = (string?)null,
      detection = new { referenceImageId = "home_button", confidence = 0.99, offsetX = 0, offsetY = 0 },
      Steps = new[] { new { Type = "Action", TargetId = actionId, Order = 1 } }
    };
    var cResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), cmdReq);
    cResp.EnsureSuccessStatusCode();
    var cmd = await cResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var commandId = cmd!["id"]!.ToString();

    // Create a session (stub screen source)
    var sResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId });
    sResp.EnsureSuccessStatusCode();
    var s = await sResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var sessionId = s!["id"]!.ToString();

    // Force execute the command; detection should resolve coordinates and accept inputs
    var exec = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), content: null);
    exec.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var execRaw = await exec.Content.ReadAsStringAsync();
    using (var doc = System.Text.Json.JsonDocument.Parse(execRaw)) {
      var root = doc.RootElement;
      var accepted = root.GetProperty("accepted").GetInt32();
      accepted.Should().BeGreaterThan(0);
    }
  }
}
