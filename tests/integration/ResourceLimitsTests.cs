using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class ResourceLimitsTests
{
    public ResourceLimitsTests()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        TestEnvironment.PrepareCleanDataDir();
    }

    [Fact]
    public async Task CreatingSessionBeyondCapacityReturns429()
    {
        // Configure capacity to 1 via environment so the app binds it on startup
        Environment.SetEnvironmentVariable("Service__Sessions__MaxConcurrentSessions", "1");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var devs = await client.GetFromJsonAsync<List<Dictionary<string, object>>>(new Uri("/adb/devices", UriKind.Relative)).ConfigureAwait(true);
    if (devs is null || devs.Count == 0) return;
    var serial = devs[0]["serial"]!.ToString();
    var first = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId = "g1", adbSerial = serial }).ConfigureAwait(true);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

    var second = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId = "g2", adbSerial = serial }).ConfigureAwait(true);
        second.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task IdleSessionIsEvictedAfterTimeout()
    {
        // Set idle timeout to 1 second for test
        Environment.SetEnvironmentVariable("Service__Sessions__IdleTimeoutSeconds", "1");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var devs2 = await client.GetFromJsonAsync<List<Dictionary<string, object>>>(new Uri("/adb/devices", UriKind.Relative)).ConfigureAwait(true);
    if (devs2 is null || devs2.Count == 0) return;
    var serial2 = devs2[0]["serial"]!.ToString();
    var createResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId = "g1", adbSerial = serial2 }).ConfigureAwait(true);
        createResp.EnsureSuccessStatusCode();
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var id = created!["id"].ToString();

        // Wait beyond idle timeout
    await Task.Delay(1500).ConfigureAwait(true);

    var getResp = await client.GetAsync(new Uri($"/sessions/{id}", UriKind.Relative)).ConfigureAwait(true);
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
