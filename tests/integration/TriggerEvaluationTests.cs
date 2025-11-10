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

        // Create game and profile
        var gameResp = await client.PostAsJsonAsync("/games", new { name = "G", description = "d" });
        gameResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var gameId = game!["id"]!.ToString();

        var profResp = await client.PostAsJsonAsync("/profiles", new { name = "P", gameId, steps = Array.Empty<object>() });
        profResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var prof = await profResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var profileId = prof!["id"]!.ToString();

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
        var tResp = await client.PostAsJsonAsync($"/profiles/{profileId}/triggers", trigCreate);
        tResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tBody = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var triggerId = tBody!["id"]!.ToString();

        // Test trigger (Pending because screen source returns null by default)
        var testResp = await client.PostAsync($"/profiles/{profileId}/triggers/{triggerId}/test", null);
        testResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var res = await testResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        ((System.Text.Json.JsonElement)res!["status"]).GetString().Should().Be("Pending");
    }
}
