using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class GamesActionsTests {
  public GamesActionsTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task CanCreateAndGetGame() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var create = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "Game A", description = "desc" }).ConfigureAwait(true);
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var game = await create.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var id = game!["id"].ToString();
    id.Should().NotBeNullOrWhiteSpace();

    var get = await client.GetAsync(new Uri($"/api/games/{id}", UriKind.Relative)).ConfigureAwait(true);
    get.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await get.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    fetched!["name"].ToString().Should().Be("Game A");
  }

  [Fact]
  public async Task ActionAuthoringRoutesAreRemoved() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createResp = await client.PostAsJsonAsync(new Uri("/api/actions", UriKind.Relative), new { name = "A1" }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

    var listResp = await client.GetAsync(new Uri("/api/actions", UriKind.Relative)).ConfigureAwait(true);
    listResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task LegacyExecuteActionSessionRouteIsRemoved() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "Game B", description = "desc" }).ConfigureAwait(true);
    gameResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var gameId = game!["id"]!.ToString();

    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(true);
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var sessionId = session!["id"]!.ToString();

    var executeResp = await client.PostAsync(new Uri($"/api/sessions/{sessionId}/execute-action?actionId=legacy", UriKind.Relative), content: null).ConfigureAwait(true);
    executeResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }
}
