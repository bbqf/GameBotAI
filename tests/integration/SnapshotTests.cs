using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class SnapshotTests
{
    [Fact]
    public async Task Snapshot_returns_png()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var createResp = await client.PostAsJsonAsync("/sessions", new { gameId = "test-game" });
        var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var id = created!["id"].ToString();

        var resp = await client.GetAsync($"/sessions/{id}/snapshot");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var content = await resp.Content.ReadAsByteArrayAsync();
        content.Length.Should().BeGreaterThan(0);
    }
}
