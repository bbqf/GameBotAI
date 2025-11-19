using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class CommandCycleDetectionTests {
  public CommandCycleDetectionTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task ForceExecuteReturnsBadRequestWhenCycleDetected() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Create a game
    var gameResp = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "CycleGame", description = "desc" }).ConfigureAwait(true);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var gameId = game!["id"]!.ToString();

    // Create two empty commands (no steps yet)
    var cAResp = await client.PostAsJsonAsync(new Uri("/commands", UriKind.Relative), new { name = "A", steps = Array.Empty<object>() }).ConfigureAwait(true);
    cAResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var cmdA = await cAResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var commandAId = cmdA!["id"]!.ToString();

    var cBResp = await client.PostAsJsonAsync(new Uri("/commands", UriKind.Relative), new { name = "B", steps = Array.Empty<object>() }).ConfigureAwait(true);
    cBResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var cmdB = await cBResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var commandBId = cmdB!["id"]!.ToString();

    // Patch A to reference B
    var patchA = new {
      steps = new[] { new { type = "Command", targetId = commandBId, order = 1 } }
    };
    var pAResp = await client.PatchAsJsonAsync(new Uri($"/commands/{commandAId}", UriKind.Relative), patchA).ConfigureAwait(true);
    pAResp.EnsureSuccessStatusCode();

    // Patch B to reference A (cycle)
    var patchB = new {
      steps = new[] { new { type = "Command", targetId = commandAId, order = 1 } }
    };
    var pBResp = await client.PatchAsJsonAsync(new Uri($"/commands/{commandBId}", UriKind.Relative), patchB).ConfigureAwait(true);
    pBResp.EnsureSuccessStatusCode();

    // Create a session
    var sResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(true);
    sResp.EnsureSuccessStatusCode();
    var session = await sResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var sessionId = session!["id"]!.ToString();

    // Force execute should detect cycle and return 400 with code cycle_detected
    var execResp = await client.PostAsync(new Uri($"/commands/{commandAId}/force-execute?sessionId={sessionId}", UriKind.Relative), content: null).ConfigureAwait(true);
    execResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var errBody = await execResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    errBody.Should().NotBeNull();
    var error = (System.Text.Json.JsonElement)errBody!["error"];
    error.GetProperty("code").GetString().Should().Be("cycle_detected");
  }
}
