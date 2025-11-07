using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public class OpenApiContractTests
{
    public OpenApiContractTests()
    {
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    }

    [Fact]
    public async Task SwaggerDocumentContainsHealthEndpoint()
    {
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        doc.Should().NotBeNull();
        doc!.ContainsKey("paths").Should().BeTrue();
        var pathsElement = (System.Text.Json.JsonElement)doc["paths"];
        pathsElement.ToString().Should().Contain("/health");
    }
}
