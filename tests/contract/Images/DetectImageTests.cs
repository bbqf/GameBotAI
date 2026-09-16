using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.Images;

public sealed class DetectImageTests {
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  // A 16x16 image with variation in every channel. Normalized correlation is undefined for a
  // constant patch, so the single-pixel image above cannot be scored — feature 085's graded-score
  // test needs real texture on both sides of the match.
  private const string TexturedPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAMbSURBVDhPARAD7/wAAP8AEO8XIN8uMM9FQL9cUK9zYJ+KcI+hgH+4kG/PoF/msE/9wD8U0C8r4B9C8A9ZAAf4CxfoIifYOTfIUEe4Z1eofmeYlXeIrId4w5do2qdY8bdICMc4H9coNucYTfcIZAAO8RYe4S0u0UQ+wVtOsXJeoYlukaB+gbeOcc6eYeWuUfy+QRPOMSreIUHuEVj+AW8AFeohJdo4NcpPRbpmVap9ZZqUdYqrhXrClWrZpVrwtUoHxToe1So15RpM9QpjBfp6ABzjLCzTQzzDWkyzcVyjiGyTn3yDtoxzzZxj5KxT+7xDEswzKdwjQOwTV/wDbgzzhQAj3DczzE5DvGVTrHxjnJNzjKqDfMGTbNijXO+zTAbDPB3TLDTjHEvzDGID/HkT7JAAKtVCOsVZSrVwWqWHapWeeoW1inXMmmXjqlX6ukURyjUo2iU/6hVW+gVtCvWEGuWbADHOTUG+ZFGue2GeknGOqYF+wJFu16Fe7rFOBcE+HNEuM+EeSvEOYQH+eBHujyHepgA4x1hIt29Yp4Zol514h7SId8uYZ+KoV/m4RxDINyfYJz7oF1X4B2wI94MY55oo17EAP8BjT7B6X6CRb5Cof4C/j3DWn2Dtr1AEv0AbzzAy3yBJ7xBg/wB3D/COH+ClL9C8AEa5blaphWaZnHaJs4Z5ypZp4aZZ+LZJD8Y5JtYpPeYZVPYJawb5ghbpmSbZsDbJxwBNsnldopBtkqd9gr6NctWdYuytUgO9QhrNMjHdIkjtEl/9AnYN8o0d4qQt0rs9wtIAVKuEZJubdIuyhHvJlGvgpFv3tEsOxDsl1Cs85BtT9AtqBPuBFOuYJNuvNMvGRLvdAFukj2uUpnuEvYt01Jtk66tUArtEGcs0MNskR+sUXvsEdQv0jBvkoyvUujvE0Uu06ABinZpyjbGCfciSbd+iXfayTQ3CPSTSLTviHVLyDWkC/YAS7Zci3a4yzcVCvdxSrfMAaZaleYa8iXbTmWbqqVYBuUYYyTYv2SZG6RZd+QZ0CfaLGeaiKda5OcbQSbbnWab+BSN+EFjBiWAAAAAASUVORK5CYII=";

  public DetectImageTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
  }

  [Fact]
  public async Task DetectUsesDefaultsAndReturnsMatchesShape() {
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

    // Feature 089: additive mask reporting. An image with no transparency is not masked, and says
    // so — every existing field above is unchanged.
    root.TryGetProperty("masked", out var masked).Should().BeTrue();
    masked.GetBoolean().Should().BeFalse();
    root.TryGetProperty("retainedPixelCount", out var retainedPixelCount).Should().BeTrue();
    retainedPixelCount.GetInt32().Should().Be(0);

    matches.ValueKind.Should().Be(JsonValueKind.Array);
    foreach (var m in matches.EnumerateArray()) {
      m.TryGetProperty("templateId", out var templateId).Should().BeTrue();
      templateId.GetString().Should().Be("tpl");

      m.TryGetProperty("score", out var score).Should().BeTrue();
      score.GetDouble().Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(1);

      foreach (var prop in new[] { "x", "y", "width", "height", "overlap" }) {
        m.TryGetProperty(prop, out var val).Should().BeTrue();
        val.GetDouble().Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(1);
      }
    }
  }

  [Fact]
  public async Task DetectAllowsParameterOverrides() {
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
    json.Matches.Should().AllSatisfy(m => {
      m.TemplateId.Should().Be("tpl");
      m.Overlap.Should().BeInRange(0, 1);
      m.Score.Should().BeInRange(0, 1);
      m.X.Should().BeInRange(0, 1);
      m.Y.Should().BeInRange(0, 1);
      m.Width.Should().BeInRange(0, 1);
      m.Height.Should().BeInRange(0, 1);
    });
  }

  // ---- Feature 085 / issue #176: device-scoped detection ---------------------------------
  //
  // Before this feature the endpoint answered an unresolvable or unknown screen with
  // 200 {"matches":[]} — a fabricated "absent" indistinguishable from a real one. The tests below
  // pin the explicit outcomes, and the two tests above pin the unchanged implicit path.

  [Fact(DisplayName = "Supplying both captureId and sessionId is rejected, not silently resolved")]
  public async Task DetectRejectsBothTargetsNamed() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = "tpl", captureId = "cap-1", sessionId = "sess-1" });

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ReadCodeAsync(resp)).Should().Be("invalid_request");
  }

  [Fact(DisplayName = "An unknown captureId is a 404, never a fallback to whatever screen is available")]
  public async Task DetectUnknownCaptureIdIsNotFound() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    // A usable stub screen exists here, so before feature 085 this returned 200 with an empty
    // match array: the captureId was ignored entirely. Substituting a different screen for the one
    // the caller named is precisely the silent-wrong-answer behaviour issue #176 reported.
    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = "tpl", captureId = "no-such-capture" });

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ReadCodeAsync(resp)).Should().Be("capture_not_found");
  }

  [Fact(DisplayName = "An unknown sessionId is a 404")]
  public async Task DetectUnknownSessionIdIsNotFound() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = "tpl", sessionId = "no-such-session" });

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ReadCodeAsync(resp)).Should().Be("session_not_found");
  }

  [Fact(DisplayName = "A named capture is measured with the caller's own low threshold, returning a graded score")]
  public async Task DetectAgainstNamedCaptureHonoursLowThreshold() {
    // SC-006 / FR-010: the absence probe this feature exists to protect needs a *graded* score on
    // the named-target path — "present but weak" must not be suppressed into an empty match set the
    // way detect-all's fixed gate would suppress it.
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = TexturedPngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    // Seed a capture directly: the screenshot route needs a live emulator, which a contract host has
    // no business starting. CaptureSessionStore is a singleton, so this is the very store the
    // endpoint resolves against.
    var captures = app.Services.GetRequiredService<GameBot.Service.Services.CaptureSessionStore>();
    var capture = captures.Add(Convert.FromBase64String(TexturedPngBase64));

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = "tpl", captureId = capture.Id, threshold = 0.1, maxResults = 1 });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await resp.Content.ReadFromJsonAsync<DetectResponseShape>();
    json.Should().NotBeNull();
    json!.Matches.Should().NotBeEmpty("a low threshold must yield a graded score, not a suppressed one");
    json.Matches.Should().AllSatisfy(m => {
      m.TemplateId.Should().Be("tpl");
      m.Score.Should().BeInRange(0, 1);
    });
  }

  [Theory(DisplayName = "Blank target values count as absent, leaving the implicit path unchanged")]
  [InlineData("")]
  [InlineData("   ")]
  public async Task DetectTreatsBlankTargetsAsAbsent(string blank) {
    // Guards FR-013: a naive client sending "" must not be diverted off the path it used before.
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = "tpl", captureId = blank, sessionId = blank });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact(DisplayName = "Omitting both target fields behaves exactly as before the feature")]
  public async Task DetectWithoutTargetIsUnchanged() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    up.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), new { referenceImageId = "tpl" });

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await resp.Content.ReadFromJsonAsync<DetectResponseShape>();
    json.Should().NotBeNull();
    json!.LimitsHit.Should().BeFalse();
  }

  private static async Task<string?> ReadCodeAsync(HttpResponseMessage resp) {
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
  }

  private sealed record DetectResponseShape(DetectMatch[] Matches, bool LimitsHit);

  private sealed record DetectMatch(string TemplateId, double Score, double X, double Y, double Width, double Height, double Overlap);
}
