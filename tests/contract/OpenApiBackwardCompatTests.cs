using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class OpenApiBackwardCompatTests {
  public OpenApiBackwardCompatTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
  }

  [Fact]
  public async Task SwaggerPublishesCanonicalImageRoutes() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    json.Should().NotBeNull();
    var paths = (JsonElement)json!["paths"];
    var text = paths.ToString();

    text.Should().Contain("\"/api/images\"");
    text.Should().Contain("\"/api/images/{id}\"");
    text.Should().Contain("\"/api/ocr/coverage\"");
    text.Should().Contain("\"/api/images/detect\"");

    // Legacy roots should no longer be published
  }

  [Fact]
  public async Task LegacyActionsRouteReturnsGuidance() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var resp = await client.GetAsync(new Uri("/actions", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.Gone);

    var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
    payload.TryGetProperty("error", out var error).Should().BeTrue();
    error.GetProperty("code").GetString().Should().Be("legacy_route");
    error.GetProperty("hint").GetString().Should().Be("/api/actions");
  }
}
