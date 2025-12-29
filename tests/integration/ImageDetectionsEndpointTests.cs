using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class ImageDetectionsEndpointTests {
  // 1x1 PNG white
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public ImageDetectionsEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    // Provide a screenshot image (same as template) so detector finds a single match.
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task DetectReturnsSingleMatch() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Seed template
    var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var detectResp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tpl", threshold = 0.5 });
    detectResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var raw = await detectResp.Content.ReadAsStringAsync();
    using var doc = System.Text.Json.JsonDocument.Parse(raw);
    var root = doc.RootElement;
    var matches = root.GetProperty("matches");
    matches.GetArrayLength().Should().Be(1);
    var first = matches[0];
    first.GetProperty("confidence").GetDouble().Should().BeGreaterThan(0.9);
    var bbox = first.GetProperty("bbox");
    bbox.GetProperty("x").GetDouble().Should().BeInRange(0, 0.0001);
    bbox.GetProperty("y").GetDouble().Should().BeInRange(0, 0.0001);
    bbox.GetProperty("width").GetDouble().Should().BeApproximately(1.0, 0.0001);
    bbox.GetProperty("height").GetDouble().Should().BeApproximately(1.0, 0.0001);
  }

  [Fact]
  public async Task DetectNormalizedBoxMatchesExpectedRatio() {
    static string B64PngFromBitmap(System.Drawing.Bitmap bmp) {
      using var ms = new System.IO.MemoryStream();
      bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
      return Convert.ToBase64String(ms.ToArray());
    }

    // Build a 4x4 screenshot containing a 2x2 non-uniform pattern at (0,0)
    using var screenshot = new System.Drawing.Bitmap(4, 4, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
    using (var g = System.Drawing.Graphics.FromImage(screenshot)) {
      g.Clear(System.Drawing.Color.White);
    }
    // Pattern values: black/white; white/black to avoid uniform template
    screenshot.SetPixel(0, 0, System.Drawing.Color.Black);
    screenshot.SetPixel(1, 0, System.Drawing.Color.White);
    screenshot.SetPixel(0, 1, System.Drawing.Color.White);
    screenshot.SetPixel(1, 1, System.Drawing.Color.Black);

    using var template = new System.Drawing.Bitmap(2, 2, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
    using (var tg = System.Drawing.Graphics.FromImage(template)) { tg.Clear(System.Drawing.Color.White); }
    template.SetPixel(0, 0, System.Drawing.Color.Black);
    template.SetPixel(1, 0, System.Drawing.Color.White);
    template.SetPixel(0, 1, System.Drawing.Color.White);
    template.SetPixel(1, 1, System.Drawing.Color.Black);

    var screenB64 = B64PngFromBitmap(screenshot);
    var tplB64 = B64PngFromBitmap(template);

    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", screenB64);
    TestEnvironment.PrepareCleanDataDir();
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tplNorm", data = tplB64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tplNorm", threshold = 0.5 });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var raw = await resp.Content.ReadAsStringAsync();
    using var doc = System.Text.Json.JsonDocument.Parse(raw);
    var bbox = doc.RootElement.GetProperty("matches")[0].GetProperty("bbox");
    bbox.GetProperty("x").GetDouble().Should().BeInRange(0, 0.001);
    bbox.GetProperty("y").GetDouble().Should().BeInRange(0, 0.001);
    bbox.GetProperty("width").GetDouble().Should().BeApproximately(0.5, 0.05);
    bbox.GetProperty("height").GetDouble().Should().BeApproximately(0.5, 0.05);
  }

  [Fact]
  public async Task DetectReturnsEmptyWhenNoScreenshot() {
    // Clear screenshot env so screen source returns null.
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", "");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    // Need fresh data dir to avoid previous template side-effects
    TestEnvironment.PrepareCleanDataDir();
    // Seed template
    var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl2", data = OneByOnePngBase64 });
    uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var detectResp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tpl2" });
    detectResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var raw = await detectResp.Content.ReadAsStringAsync();
    using var doc = System.Text.Json.JsonDocument.Parse(raw);
    var root = doc.RootElement;
    root.GetProperty("matches").GetArrayLength().Should().Be(0);
    root.GetProperty("limitsHit").GetBoolean().Should().BeFalse();
  }
}
