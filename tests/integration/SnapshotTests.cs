using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

internal class SnapshotTests {
  public SnapshotTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task SnapshotReturnsPng() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var devs = await client.GetFromJsonAsync<List<Dictionary<string, object>>>(new Uri("/adb/devices", UriKind.Relative)).ConfigureAwait(true);
    if (devs is null || devs.Count == 0) return;
    var serial = devs[0]["serial"]!.ToString();
    var createResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId = "test-game", adbSerial = serial }).ConfigureAwait(true);
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var id = created!["id"].ToString();

    var resp = await client.GetAsync(new Uri($"/sessions/{id}/snapshot", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    resp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
    var content = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
    content.Length.Should().BeGreaterThan(0);
  }
}
