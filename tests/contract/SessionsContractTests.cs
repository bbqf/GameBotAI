using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public class SessionsContractTests
{
    [Fact]
    public async Task Create_Get_Snapshot_Delete_flow_is_exposed()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        // Create session
        var createResp = await client.PostAsJsonAsync("/sessions", new { gameId = "test-game" });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        created.Should().NotBeNull();
        var id = created!["id"].ToString();
        id.Should().NotBeNullOrWhiteSpace();

        // Get session
        var getResp = await client.GetAsync($"/sessions/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Snapshot
        var snapResp = await client.GetAsync($"/sessions/{id}/snapshot");
        snapResp.StatusCode.Should().Be(HttpStatusCode.OK);
        snapResp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        // Delete
        var delResp = await client.DeleteAsync($"/sessions/{id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
