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
  public async Task LegacyPathsStillPresent() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    json.Should().NotBeNull();
    var paths = (JsonElement)json!["paths"];
    var text = paths.ToString();

    // Minimal legacy surface: images CRUD, health, OCR coverage
    text.Should().Contain("\"/images\"");
    text.Should().Contain("\"/images/{id}\"");
    text.Should().Contain("\"/health\"");
    text.Should().Contain("\"/api/ocr/coverage\"");

    // New additive endpoint present too
    text.Should().Contain("\"/images/detect\"");
  }
}
