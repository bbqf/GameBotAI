using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

internal class HealthEndpointTests {
  public HealthEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task HealthReturnsOk() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/health", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>().ConfigureAwait(true);
    json.Should().NotBeNull();
    json!["status"].Should().Be("ok");
  }
}
