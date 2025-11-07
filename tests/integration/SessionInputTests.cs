using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class SessionInputTests
{
    public SessionInputTests()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    }

    [Fact]
    public async Task PostingInputsAcceptsActions()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createResp = await client.PostAsJsonAsync("/sessions", new { gameId = "test-game" }).ConfigureAwait(true);
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var id = created!["id"].ToString();

        var actions = new { actions = new object[] { new { type = "key", args = new { keyCode = 19 } }, new { type = "tap", args = new { x = 10, y = 10 } } } };
    var resp = await client.PostAsJsonAsync($"/sessions/{id}/inputs", actions).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>().ConfigureAwait(true);
        body!["accepted"].Should().Be(2);
    }
}
