using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class SessionInputTests {
  public SessionInputTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task PostingInputsAcceptsActions() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Check devices and skip if none to avoid flakiness without ADB
    // Force stub mode for inputs test to avoid external ADB process hangs
    // Do NOT request adbSerial so session runs without ADB even if devices exist
    var createResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId = "test-game" }).ConfigureAwait(true);
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var id = created!["id"].ToString();

    var actions = new {
      actions = new[]
    {
            new { type = "key", args = new Dictionary<string, object>{{"key", "ESCAPE"}} },
            new { type = "key", args = new Dictionary<string, object>{{"keyCode", 29}} }, // 'A'
            new { type = "tap", args = new Dictionary<string, object>{{"x", 10},{"y", 10}} }
        }
    };
    using var content = JsonContent.Create(actions);
    var resp = await client.PostAsync(new Uri($"/sessions/{id}/inputs", UriKind.Relative), content).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>().ConfigureAwait(true);
    body!["accepted"].Should().Be(3);
  }
}
