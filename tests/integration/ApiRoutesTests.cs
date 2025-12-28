using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class ApiRoutesTests
{
    public ApiRoutesTests()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        TestEnvironment.PrepareCleanDataDir();
    }

    [Fact]
    public async Task CanonicalActionsRouteSucceeds()
    {
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var resp = await client.GetAsync(new Uri("/api/actions", UriKind.Relative)).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LegacyActionsRouteReturnsGuidance()
    {
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var resp = await client.GetAsync(new Uri("/actions", UriKind.Relative)).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.Gone);

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        payload.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetString().Should().Be("legacy_route");
        error.GetProperty("hint").GetString().Should().Be("/api/actions");
    }
}
