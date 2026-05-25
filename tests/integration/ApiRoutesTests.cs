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
    public async Task RemovedActionsRoutesReturnNotFound()
    {
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var canonicalResp = await client.GetAsync(new Uri("/api/actions", UriKind.Relative)).ConfigureAwait(true);
        canonicalResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var legacyResp = await client.GetAsync(new Uri("/actions", UriKind.Relative)).ConfigureAwait(true);
        legacyResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
