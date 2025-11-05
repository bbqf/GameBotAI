using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class ResourceLimitsTests
{
    [Fact]
    public async Task Creating_session_beyond_capacity_returns_429()
    {
        // Configure capacity to 1 via environment so the app binds it on startup
        Environment.SetEnvironmentVariable("Service__Sessions__MaxConcurrentSessions", "1");
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var first = await client.PostAsJsonAsync("/sessions", new { gameId = "g1" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/sessions", new { gameId = "g2" });
        second.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task Idle_session_is_evicted_after_timeout()
    {
        // Set idle timeout to 1 second for test
        Environment.SetEnvironmentVariable("Service__Sessions__IdleTimeoutSeconds", "1");
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var createResp = await client.PostAsJsonAsync("/sessions", new { gameId = "g1" });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = created!["id"].ToString();

        // Wait beyond idle timeout
        await Task.Delay(1500);

        var getResp = await client.GetAsync($"/sessions/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
