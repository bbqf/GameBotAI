using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class ImageEndpointsErrorTests
{
  public ImageEndpointsErrorTests()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task PostMissingFieldsReturnsInvalidRequest()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var resp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "", data = "" });
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    json!.Should().ContainKey("error");
  }

  [Fact]
  public async Task PostInvalidBase64ReturnsInvalidImage()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var resp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "badimg", data = "not-base64" });
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task GetMissingReturnsNotFound()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var resp = await client.GetAsync(new Uri("/api/images/does-not-exist", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task DeleteMissingReturnsNotFound()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var resp = await client.DeleteAsync(new Uri("/api/images/does-not-exist", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task PostOverwriteSetsFlag()
  {
    // 1x1 PNG white
    const string oneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var first = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "over", data = oneByOnePngBase64 });
    first.StatusCode.Should().Be(HttpStatusCode.Created);
    var second = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "over", data = oneByOnePngBase64 });
    second.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await second.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    body!.Should().ContainKey("overwrite");
  }
}
