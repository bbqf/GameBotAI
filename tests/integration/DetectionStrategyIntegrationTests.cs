using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class DetectionStrategyIntegrationTests : IDisposable {
    private readonly string? _prevUseAdb;
    private readonly string? _prevDynamicPort;
    private readonly string? _prevAuthToken;
    private readonly string? _prevDataDir;

    private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

    public DetectionStrategyIntegrationTests() {
        _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
        _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
        _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
        _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
        TestEnvironment.PrepareCleanDataDir();
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
        Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
        Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", null);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ForceExecuteUsesFirstMatchStrategy() {
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        // Persist reference image
        var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "home_button", data = OneByOnePngBase64 });
        uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Create game
        var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "DetectStrategyGame", description = "desc" });
        gameResp.EnsureSuccessStatusCode();
        var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var gameId = game![(string)"id"]!.ToString();

        // Create action with single tap
        var actionReq = new {
            name = "TapByDetectFirst",
            gameId,
            steps = new[] { new { type = "tap", args = new Dictionary<string, object>{{"x", 1}, {"y", 1}}, delayMs = (int?)null, durationMs = (int?)null } }
        };
        var aResp = await client.PostAsJsonAsync(new Uri("/api/actions", UriKind.Relative), actionReq);
        aResp.EnsureSuccessStatusCode();
        var act = await aResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var actionId = act![(string)"id"]!.ToString();

        // Create command with detection using FirstMatch
        var cmdReq = new {
            name = "DetectAndTapFirstMatch",
            triggerId = (string?)null,
            detection = new { referenceImageId = "home_button", confidence = 0.80, offsetX = 0, offsetY = 0, selectionStrategy = "FirstMatch" },
            steps = new[] { new { type = "Action", targetId = actionId, order = 1 } }
        };
        var cResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), cmdReq);
        cResp.EnsureSuccessStatusCode();
        var cmd = await cResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var commandId = cmd![(string)"id"]!.ToString();

        // Create session
        var sResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId });
        sResp.EnsureSuccessStatusCode();
        var s = await sResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var sessionId = s![(string)"id"]!.ToString();

        // Force execute
        var exec = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), content: null);
        exec.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var execRaw = await exec.Content.ReadAsStringAsync();
        using (var doc = System.Text.Json.JsonDocument.Parse(execRaw)) {
            var root = doc.RootElement;
            var accepted = root.GetProperty("accepted").GetInt32();
            accepted.Should().BeGreaterThan(0);
        }
    }
}
