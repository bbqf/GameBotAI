using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public class OpenApiContractTests
{
    [Fact]
    public async Task Swagger_document_contains_health_endpoint()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        var resp = await client.GetAsync("/swagger/v1/swagger.json");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await resp.Content.ReadAsStringAsync();
        content.Should().Contain("/health");
    }
}
