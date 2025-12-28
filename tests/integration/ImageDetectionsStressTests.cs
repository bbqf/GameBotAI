using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class ImageDetectionsStressTests {
  private static string Tile2x2Into16x16() {
    using var base2 = new System.Drawing.Bitmap(2, 2);
    base2.SetPixel(0,0, System.Drawing.Color.Black);
    base2.SetPixel(1,0, System.Drawing.Color.White);
    base2.SetPixel(0,1, System.Drawing.Color.White);
    base2.SetPixel(1,1, System.Drawing.Color.Black);
    using var big = new System.Drawing.Bitmap(16, 16);
    using (var g = System.Drawing.Graphics.FromImage(big)) {
      for (int y=0; y<16; y+=2) {
        for (int x=0; x<16; x+=2) {
          g.DrawImage(base2, x, y);
        }
      }
    }
    using var ms = new System.IO.MemoryStream();
    big.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return Convert.ToBase64String(ms.ToArray());
  }
  private static string TwoByTwoPatternB64() {
    using var bmp = new System.Drawing.Bitmap(2, 2);
    bmp.SetPixel(0,0, System.Drawing.Color.Black);
    bmp.SetPixel(1,0, System.Drawing.Color.White);
    bmp.SetPixel(0,1, System.Drawing.Color.White);
    bmp.SetPixel(1,1, System.Drawing.Color.Black);
    using var ms = new System.IO.MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return Convert.ToBase64String(ms.ToArray());
  }

  public ImageDetectionsStressTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task ManyMatchesAreCappedAndLimitsHitFlagSet() {
    // Build a 4x4 screenshot composed of repeating 2x2 pattern so matches are many
    var screenB64 = Tile2x2Into16x16();
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", screenB64);

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Seed template using same 2x2 pattern
    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tplStress", data = TwoByTwoPatternB64() });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tplStress", threshold = 0.5, maxResults = 1 });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var matches = (System.Text.Json.JsonElement)json!["matches"];
    matches.GetArrayLength().Should().Be(1);
    json!.Should().ContainKey("limitsHit");
    json["limitsHit"].ToString().Should().Be("True");
  }

  [Fact]
  public async Task TimeoutReturnsOkWithLimitsHit() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Configure very small timeout to force cancel
    var prevTimeout = Environment.GetEnvironmentVariable("Service__Detections__TimeoutMs");
    Environment.SetEnvironmentVariable("Service__Detections__TimeoutMs", "1");
    try {
      var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "missing" });
      resp.StatusCode.Should().Be(HttpStatusCode.NotFound); // missing ref image short-circuits

      // Seed and re-run with small timeout
      const string oneByOne = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";
      var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tplTimeout", data = oneByOne });
      up.StatusCode.Should().Be(HttpStatusCode.Created);

      // Provide a larger screen to increase work
      Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", Tile2x2Into16x16());
      var resp2 = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tplTimeout" });
      resp2.StatusCode.Should().Be(HttpStatusCode.OK);
      var payload = await resp2.Content.ReadFromJsonAsync<Dictionary<string, object>>();
      payload!["limitsHit"].ToString().Should().Be("True");
    }
    finally {
      Environment.SetEnvironmentVariable("Service__Detections__TimeoutMs", prevTimeout);
    }
  }
}
