using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class ImageDetectionsContractTests {
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public ImageDetectionsContractTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
  }

  [Fact]
  public async Task SwaggerDocumentContainsDetectionsEndpoint() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    json.Should().NotBeNull();
    json!.ContainsKey("paths").Should().BeTrue();
    var paths = (JsonElement)json["paths"];
    var text = paths.ToString();
    text.Should().Contain("\"/api/images/detect\"");
  }

  [Fact]
  public async Task DetectResponseMatchesSchemaShape() {
    // Provide screenshot so detection can produce a match (shape invariants hold either way)
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Seed reference image
    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tpl" });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var raw = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(raw);
    var root = doc.RootElement;

    // Required properties
    root.TryGetProperty("matches", out var matches).Should().BeTrue();
    root.TryGetProperty("limitsHit", out var limitsHit).Should().BeTrue();
    // Expect a boolean value
    (limitsHit.ValueKind == JsonValueKind.True || limitsHit.ValueKind == JsonValueKind.False).Should().BeTrue();

    // matches is array; each item has bbox{ x,y,width,height in [0,1] } and confidence in [0,1]
    matches.ValueKind.Should().Be(JsonValueKind.Array);
    foreach (var m in matches.EnumerateArray()) {
      m.TryGetProperty("confidence", out var conf).Should().BeTrue();
      conf.ValueKind.Should().Be(JsonValueKind.Number);
      var c = conf.GetDouble();
      c.Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(1);

      m.TryGetProperty("bbox", out var bbox).Should().BeTrue();
      bbox.TryGetProperty("x", out var x).Should().BeTrue();
      bbox.TryGetProperty("y", out var y).Should().BeTrue();
      bbox.TryGetProperty("width", out var w).Should().BeTrue();
      bbox.TryGetProperty("height", out var h).Should().BeTrue();
      foreach (var v in new[]{ x, y, w, h }) {
        v.ValueKind.Should().Be(JsonValueKind.Number);
        var dv = v.GetDouble();
        dv.Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(1);
      }
    }
  }
}
