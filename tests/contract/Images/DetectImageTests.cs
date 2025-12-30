using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Images;

public sealed class DetectImageTests
{
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public DetectImageTests()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
  }

  [Fact]
  public async Task DetectUsesDefaultsAndReturnsMatchesShape()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tpl" });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var raw = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(raw);
    var root = doc.RootElement;

    root.TryGetProperty("matches", out var matches).Should().BeTrue();
    root.TryGetProperty("limitsHit", out var limitsHit).Should().BeTrue();
    (limitsHit.ValueKind == JsonValueKind.True || limitsHit.ValueKind == JsonValueKind.False).Should().BeTrue();

    matches.ValueKind.Should().Be(JsonValueKind.Array);
    foreach (var m in matches.EnumerateArray())
    {
      m.TryGetProperty("templateId", out var templateId).Should().BeTrue();
      templateId.GetString().Should().Be("tpl");

      m.TryGetProperty("score", out var score).Should().BeTrue();
      score.GetDouble().Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(1);

      foreach (var prop in new[] { "x", "y", "width", "height", "overlap" })
      {
        m.TryGetProperty(prop, out var val).Should().BeTrue();
        val.GetDouble().Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(1);
      }
    }
  }

  [Fact]
  public async Task DetectAllowsParameterOverrides()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tpl", threshold = 0.5, maxResults = 2, overlap = 0.25 });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var json = await resp.Content.ReadFromJsonAsync<DetectResponseShape>();
    json.Should().NotBeNull();
    json!.LimitsHit.Should().BeFalse();
    json.Matches.Should().AllSatisfy(m =>
    {
      m.TemplateId.Should().Be("tpl");
      m.Overlap.Should().BeInRange(0, 1);
      m.Score.Should().BeInRange(0, 1);
      m.X.Should().BeInRange(0, 1);
      m.Y.Should().BeInRange(0, 1);
      m.Width.Should().BeInRange(0, 1);
      m.Height.Should().BeInRange(0, 1);
    });
  }

  private sealed record DetectResponseShape(DetectMatch[] Matches, bool LimitsHit);

  private sealed record DetectMatch(string TemplateId, double Score, double X, double Y, double Width, double Height, double Overlap);
}
