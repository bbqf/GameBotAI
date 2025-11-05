using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class ResourceLimitsTests
{
    [Fact]
    public async Task CreatingSessionBeyondCapacityReturns429()
    {
        // Configure capacity to 1 via environment so the app binds it on startup
        Environment.SetEnvironmentVariable("Service__Sessions__MaxConcurrentSessions", "1");
    using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var first = await client.PostAsJsonAsync("/sessions", new { gameId = "g1" }).ConfigureAwait(true);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

    var second = await client.PostAsJsonAsync("/sessions", new { gameId = "g2" }).ConfigureAwait(true);
        second.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task IdleSessionIsEvictedAfterTimeout()
    {
        // Set idle timeout to 1 second for test
        Environment.SetEnvironmentVariable("Service__Sessions__IdleTimeoutSeconds", "1");
    using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createResp = await client.PostAsJsonAsync("/sessions", new { gameId = "g1" }).ConfigureAwait(true);
        createResp.EnsureSuccessStatusCode();
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var id = created!["id"].ToString();

        // Wait beyond idle timeout
    await Task.Delay(1500).ConfigureAwait(true);

    var getResp = await client.GetAsync(new Uri($"/sessions/{id}", UriKind.Relative)).ConfigureAwait(true);
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
