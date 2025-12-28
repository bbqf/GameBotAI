using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class DomainMetricsEndpointTests {
  public DomainMetricsEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task DomainMetricsReflectCounts() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Initially empty counts
    var emptyResp = await client.GetAsync(new Uri("/api/metrics/domain", UriKind.Relative)).ConfigureAwait(true);
    emptyResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var emptyJson = await emptyResp.Content.ReadFromJsonAsync<Dictionary<string, int>>().ConfigureAwait(true);
    emptyJson.Should().NotBeNull();
    emptyJson!["actions"].Should().Be(0);
    emptyJson!["commands"].Should().Be(0);
    emptyJson!["triggers"].Should().Be(0);

    // Create a game
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "MetricsGame", description = "desc" }).ConfigureAwait(true);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var gameId = game!["id"]!.ToString();

    // Create action
    var actionReq = new {
      Name = "A1",
      GameId = gameId,
      Steps = new[]
        {
                new { Type = "tap", Args = new Dictionary<string, object>{{"x", 1},{"y",1}}, DelayMs = (int?)null, DurationMs = (int?)null }
            }
    };
    var aResp = await client.PostAsJsonAsync(new Uri("/api/actions", UriKind.Relative), actionReq).ConfigureAwait(true);
    aResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var act = await aResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var actionId = act!["id"]!.ToString();

    // Create trigger
    var trigReq = new { Type = "delay", Enabled = true, CooldownSeconds = 0, Params = new { seconds = 0 } };
    var tResp = await client.PostAsJsonAsync(new Uri("/api/triggers", UriKind.Relative), trigReq).ConfigureAwait(true);
    tResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var tr = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var triggerId = tr!["id"]!.ToString();

    // Create command referencing action & trigger
    var cmdReq = new {
      Name = "C1",
      TriggerId = triggerId,
      Steps = new[] { new { Type = "Action", TargetId = actionId, Order = 1 } }
    };
    var cResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), cmdReq).ConfigureAwait(true);
    cResp.StatusCode.Should().Be(HttpStatusCode.Created);

    // Fetch metrics again
    var metricsResp = await client.GetAsync(new Uri("/api/metrics/domain", UriKind.Relative)).ConfigureAwait(true);
    metricsResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await metricsResp.Content.ReadFromJsonAsync<Dictionary<string, int>>().ConfigureAwait(true);
    json.Should().NotBeNull();
    json!["actions"].Should().BeGreaterOrEqualTo(1);
    json!["commands"].Should().BeGreaterOrEqualTo(1);
    json!["triggers"].Should().BeGreaterOrEqualTo(1);
  }
}
