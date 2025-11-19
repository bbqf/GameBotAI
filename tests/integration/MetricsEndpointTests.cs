using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

internal class MetricsEndpointTests {
  public MetricsEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task MetricsEndpointReturnsSnapshot() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var resp = await client.GetAsync(new Uri("/metrics/triggers", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, long>>().ConfigureAwait(true);
    json.Should().NotBeNull();
    json!.Should().ContainKey("evaluations");
    json!.Should().ContainKey("skippedNoSessions");
    json!.Should().ContainKey("overlapSkipped");
    json!.Should().ContainKey("lastCycleDurationMs");
  }
}
