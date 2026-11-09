using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Images;

/// <summary>Feature 182: the OpenAPI document states the cache headers of GET /api/images/{id}.</summary>
public sealed class GetImageOpenApiTests {
  [Fact]
  public async Task GetImageDocumentsCacheHeadersAndNotModified() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

    var responses = document.RootElement.GetProperty("paths").GetProperty("/api/images/{id}").GetProperty("get").GetProperty("responses");
    var headers = responses.GetProperty("200").GetProperty("headers");
    headers.TryGetProperty("Cache-Control", out _).Should().BeTrue();
    headers.TryGetProperty("ETag", out _).Should().BeTrue();
    responses.TryGetProperty("304", out _).Should().BeTrue();
  }
}
