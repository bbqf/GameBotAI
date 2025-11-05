using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public class OpenApiContractTests
{
    [Fact]
    public async Task SwaggerDocumentContainsHealthEndpoint()
    {
    using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(true);
        content.Should().Contain("/health");
    }
}
