using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class GamesProfilesTests
{
    public GamesProfilesTests()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        TestEnvironment.PrepareCleanDataDir();
    }

    [Fact]
    public async Task CanCreateAndGetGame()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var create = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "Game A", description = "desc" }).ConfigureAwait(true);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var game = await create.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var id = game!["id"].ToString();
        id.Should().NotBeNullOrWhiteSpace();

        var get = await client.GetAsync(new Uri($"/games/{id}", UriKind.Relative)).ConfigureAwait(true);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await get.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        fetched!["name"].ToString().Should().Be("Game A");
    }

    [Fact]
    public async Task CanExecuteProfileAgainstSession()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Create a game
    var gameResp = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "Game B", description = "desc" }).ConfigureAwait(true);
        gameResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var gameId = game!["id"]!.ToString();

        // Create a profile with two actions
        var profileReq = new
        {
            name = "P-Exec",
            gameId,
            steps = new[]
            {
                new { type = "tap", args = new Dictionary<string, object>{{"x", 10}, {"y", 10}}, delayMs = (int?)null, durationMs = (int?)null },
                new { type = "swipe", args = new Dictionary<string, object>{{"x1", 0}, {"y1", 0}, {"x2", 100}, {"y2", 100}}, delayMs = (int?)null, durationMs = (int?)null }
            }
        };
        var pResp = await client.PostAsJsonAsync(new Uri("/profiles", UriKind.Relative), profileReq).ConfigureAwait(true);
        pResp.EnsureSuccessStatusCode();
    var prof = await pResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var profileId = prof!["id"]!.ToString();

        // Create a session
        var sResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(true);
        sResp.EnsureSuccessStatusCode();
    var s = await sResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var sessionId = s!["id"]!.ToString();

        // Execute the profile
        var execResp = await client.PostAsync(new Uri($"/sessions/{sessionId}/execute?profileId={profileId}", UriKind.Relative), content: null).ConfigureAwait(true);
        execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var execBody = await execResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var accepted = ((System.Text.Json.JsonElement)execBody!["accepted"]).GetInt32();
        accepted.Should().Be(2);
    }

    [Fact]
    public async Task CanCreateAndFilterProfilesByGame()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        // Create a game
    var g = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "Game B", description = "desc" }).ConfigureAwait(true);
    var gBody = await g.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var gameId = gBody!["id"].ToString();

        // Create two profiles, one for this game, one for another
        var profileReq = new { name = "P1", gameId, steps = new object[] { new { type = "tap", args = new { x = 1, y = 2 } } } };
    var p1 = await client.PostAsJsonAsync(new Uri("/profiles", UriKind.Relative), profileReq).ConfigureAwait(true);
        p1.StatusCode.Should().Be(HttpStatusCode.Created);

    var p2 = await client.PostAsJsonAsync(new Uri("/profiles", UriKind.Relative), new { name = "P2", gameId = "other-game", steps = Array.Empty<object>() }).ConfigureAwait(true);
        p2.StatusCode.Should().Be(HttpStatusCode.Created);

        // Filter
    var list = await client.GetFromJsonAsync<List<Dictionary<string, object>>>(new Uri($"/profiles?gameId={gameId}", UriKind.Relative)).ConfigureAwait(true);
        list!.Should().NotBeNull();
        list!.Count.Should().Be(1);
        list[0]["name"].ToString().Should().Be("P1");
    }
}
