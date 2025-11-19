using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class TriggerEvaluationTests
{
    public TriggerEvaluationTests()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        TestEnvironment.PrepareCleanDataDir();
    }

    [Fact]
    public async Task ImageTriggerTestEndpointReturnsPendingWithoutScreen()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        // Create game
    var gameResp = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "G", description = "d" });
        gameResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var gameId = game!["id"]!.ToString();


        // Create an image trigger (no actual screen; should be Pending)
        var trigCreate = new
        {
            type = "image-match",
            enabled = true,
            cooldownSeconds = 60,
            @params = new
            {
                referenceImageId = "tpl",
                region = new { x = 0.0, y = 0.0, width = 1.0, height = 1.0 },
                similarityThreshold = 0.9
            }
        };
    var tResp = await client.PostAsJsonAsync(new Uri("/triggers", UriKind.Relative), trigCreate);
        tResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tBody = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var triggerId = tBody!["id"]!.ToString();

        // Test trigger (Pending because screen source returns null by default)
    var testResp = await client.PostAsync(new Uri($"/triggers/{triggerId}/test", UriKind.Relative), null);
        testResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var res = await testResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        ((System.Text.Json.JsonElement)res!["status"]).GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task DelayTriggerRespectsCooldown()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        // Create game
    var gameResp = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "G2", description = "d" });
        gameResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var gameId = game!["id"]!.ToString();


        // Create a delay trigger with 0s delay and 2s cooldown
        var trigCreate = new { type = "delay", enabled = true, cooldownSeconds = 2, @params = new { seconds = 0 } };
    var tResp = await client.PostAsJsonAsync(new Uri("/triggers", UriKind.Relative), trigCreate);
        tResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tBody = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var triggerId = tBody!["id"]!.ToString();

        // First test -> Satisfied
    var testResp1 = await client.PostAsync(new Uri($"/triggers/{triggerId}/test", UriKind.Relative), null);
        testResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = await testResp1.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        ((System.Text.Json.JsonElement)res1!["status"]).GetString().Should().Be("Satisfied");

        // Immediate second test -> Cooldown
    var testResp2 = await client.PostAsync(new Uri($"/triggers/{triggerId}/test", UriKind.Relative), null);
        testResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = await testResp2.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        ((System.Text.Json.JsonElement)res2!["status"]).GetString().Should().Be("Cooldown");

        // After cooldown window -> Satisfied again
        // Increase delay slightly above cooldown to avoid timing flakiness on slower CI
        await Task.Delay(2500);
    var testResp3 = await client.PostAsync(new Uri($"/triggers/{triggerId}/test", UriKind.Relative), null);
        testResp3.StatusCode.Should().Be(HttpStatusCode.OK);
        var res3 = await testResp3.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        ((System.Text.Json.JsonElement)res3!["status"]).GetString().Should().Be("Satisfied");
    }
}
