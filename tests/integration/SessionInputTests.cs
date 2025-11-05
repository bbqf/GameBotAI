using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class SessionInputTests
{
    [Fact]
    public async Task Posting_inputs_accepts_actions()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var createResp = await client.PostAsJsonAsync("/sessions", new { gameId = "test-game" });
        var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = created!["id"].ToString();

        var actions = new { actions = new object[] { new { type = "key", args = new { keyCode = 19 } }, new { type = "tap", args = new { x = 10, y = 10 } } } };
        var resp = await client.PostAsJsonAsync($"/sessions/{id}/inputs", actions);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        body!["accepted"].Should().Be(2);
    }
}
