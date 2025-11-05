using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class SnapshotTests
{
    [Fact]
    public async Task SnapshotReturnsPng()
    {
    using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createResp = await client.PostAsJsonAsync("/sessions", new { gameId = "test-game" }).ConfigureAwait(true);
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var id = created!["id"].ToString();

    var resp = await client.GetAsync(new Uri($"/sessions/{id}/snapshot", UriKind.Relative)).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
    var content = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
        content.Length.Should().BeGreaterThan(0);
    }
}
