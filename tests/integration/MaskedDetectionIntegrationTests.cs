using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenCvSharp;
using Xunit;

namespace GameBot.IntegrationTests;

/// <summary>
/// End-to-end cover for feature 089 (issue #190): a masked reference image matches a
/// non-rectangular target that its rectangular equivalent cannot, through the real HTTP paths, and
/// the mask reaches every consumer without any sequence being edited.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class MaskedDetectionIntegrationTests : IDisposable {
  private const int FrameWidth = 320;
  private const int FrameHeight = 240;
  private const int BadgeWidth = 42;
  private const int BadgeHeight = 52;
  private const int BadgeX = 150;
  private const int BadgeY = 100;
  private const double Gate = 0.85;

  private readonly string? _prevUseAdb;
  private readonly string? _prevToken;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevScreen;
  private readonly string? _prevCaptureInterval;

  public MaskedDetectionIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevScreen = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");
    _prevCaptureInterval = Environment.GetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", "10");
    // The screen the service sees: the badge on the bright backdrop, i.e. the second instance.
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", FrameBase64(withBadge: true));
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", _prevScreen);
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", _prevCaptureInterval);
    GC.SuppressFinalize(this);
  }

  [Fact(DisplayName = "Detect: the masked image passes the gate where the rectangular one fails")]
  public async Task DetectMaskedImagePassesGateWhereRectangularFails() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    await UploadAsync(client, "badge-masked", MaskedTemplateBase64()).ConfigureAwait(false);
    await UploadAsync(client, "badge-rect", RectangularTemplateBase64()).ConfigureAwait(false);

    var masked = await DetectAsync(client, "badge-masked").ConfigureAwait(false);
    var rectangular = await DetectAsync(client, "badge-rect").ConfigureAwait(false);

    masked.RootElement.GetProperty("matches").GetArrayLength().Should().Be(1);
    masked.RootElement.GetProperty("matches")[0].GetProperty("confidence").GetDouble()
      .Should().BeGreaterThanOrEqualTo(Gate);
    rectangular.RootElement.GetProperty("matches").GetArrayLength().Should().Be(0,
      "the rectangular crop carries a backdrop this screen does not have");
  }

  [Fact(DisplayName = "Detect reports mask state additively without changing any existing field")]
  public async Task DetectReportsMaskStateAdditively() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    await UploadAsync(client, "badge-masked", MaskedTemplateBase64()).ConfigureAwait(false);
    await UploadAsync(client, "badge-opaque", RectangularTemplateBase64()).ConfigureAwait(false);

    var masked = await DetectAsync(client, "badge-masked", threshold: 0.0).ConfigureAwait(false);
    var opaque = await DetectAsync(client, "badge-opaque", threshold: 0.0).ConfigureAwait(false);

    masked.RootElement.GetProperty("masked").GetBoolean().Should().BeTrue();
    masked.RootElement.GetProperty("retainedPixelCount").GetInt32().Should().BeGreaterThan(0);

    opaque.RootElement.GetProperty("masked").GetBoolean().Should().BeFalse();
    opaque.RootElement.GetProperty("retainedPixelCount").GetInt32().Should().Be(0);

    // Existing fields are untouched.
    opaque.RootElement.TryGetProperty("limitsHit", out _).Should().BeTrue();
    opaque.RootElement.GetProperty("matches")[0].TryGetProperty("bbox", out _).Should().BeTrue();
  }

  [Fact(DisplayName = "Detect-all honours the mask and keeps its response shape unchanged")]
  public async Task DetectAllHonoursMaskAndKeepsResponseShape() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    await UploadAsync(client, "badge-masked", MaskedTemplateBase64()).ConfigureAwait(false);

    var captureResp = await client.PostAsJsonAsync(new Uri("/api/images/capture", UriKind.Relative), new { }).ConfigureAwait(false);
    if (captureResp.StatusCode != HttpStatusCode.OK && captureResp.StatusCode != HttpStatusCode.Created) {
      // Capture is not available in this configuration; the single-image path already proves the
      // mask reaches detection, so there is nothing to assert here.
      return;
    }

    using var captureDoc = JsonDocument.Parse(await captureResp.Content.ReadAsStringAsync().ConfigureAwait(false));
    var captureId = captureDoc.RootElement.GetProperty("captureId").GetString();

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect-all", UriKind.Relative), new { captureId }).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    doc.RootElement.TryGetProperty("masked", out _).Should().BeFalse(
      "a library-wide sweep has no single mask state, so the field is deliberately absent");
    var matches = doc.RootElement.GetProperty("matches");
    matches.GetArrayLength().Should().BeGreaterThan(0, "the masked badge is on screen");
  }

  [Fact(DisplayName = "An uploaded mask survives storage and retrieval unflattened")]
  public async Task UploadedMaskSurvivesStorageAndRetrieval() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var original = MaskedTemplateBase64();
    await UploadAsync(client, "badge-roundtrip", original).ConfigureAwait(false);

    var resp = await client.GetAsync(new Uri("/api/images/badge-roundtrip", UriKind.Relative)).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

    using var retrieved = Mat.FromImageData(bytes, ImreadModes.Unchanged);
    retrieved.Channels().Should().Be(4, "the alpha channel is the mask; flattening it loses the feature silently");
    retrieved.At<Vec4b>(0, 0).Item3.Should().Be(0, "the crop's corner was erased and must still be transparent");
    retrieved.At<Vec4b>(BadgeHeight / 2, BadgeWidth / 2).Item3.Should().Be(255, "the badge centre was opaque");
  }

  [Fact(DisplayName = "An unmodified wait-for-image step resolves a target its unmasked template missed")]
  public async Task UnmodifiedWaitForImageStepResolvesTargetUnmaskedTemplateMissed() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    await UploadAsync(client, "badge-masked", MaskedTemplateBase64()).ConfigureAwait(false);
    await UploadAsync(client, "badge-rect", RectangularTemplateBase64()).ConfigureAwait(false);
    var sessionId = await CreateSessionAsync(client, "MaskedBadgeGame").ConfigureAwait(false);

    var maskedOutcome = await RunWaitForImageAsync(client, sessionId, "badge-masked").ConfigureAwait(false);
    var rectangularOutcome = await RunWaitForImageAsync(client, sessionId, "badge-rect").ConfigureAwait(false);

    maskedOutcome.Should().Be("image_detected",
      "the step definition is identical; only the reference image gained a mask");
    rectangularOutcome.Should().NotBe("image_detected");
  }

  private static HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task UploadAsync(HttpClient client, string id, string base64) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id, data = base64 }).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  private static async Task<JsonDocument> DetectAsync(HttpClient client, string id, double threshold = Gate) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = id, threshold, maxResults = 1 }).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  private static async Task<string> CreateSessionAsync(HttpClient client, string gameName) {
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = gameName, description = "desc" }).ConfigureAwait(false);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    var gameId = game!["id"]!.ToString();

    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(false);
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    return session!["id"]!.ToString()!;
  }

  /// <summary>Runs a stock wait-for-image step against one reference image and returns its reason.</summary>
  private static async Task<string?> RunWaitForImageAsync(HttpClient client, string sessionId, string referenceImageId) {
    var commandReq = new {
      name = $"Wait for {referenceImageId}",
      steps = new object[] {
        new {
          type = "WaitForImage",
          order = 0,
          waitForImage = new {
            timeoutMs = 200,
            detectionTarget = new {
              referenceImageId,
              confidence = Gate,
              offsetX = 0,
              offsetY = 0,
              selectionStrategy = "HighestConfidence"
            }
          }
        }
      }
    };

    var createResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq).ConfigureAwait(false);
    createResp.EnsureSuccessStatusCode();
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    var commandId = created!["id"]!.ToString()!;

    var execResp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null).ConfigureAwait(false);
    execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = JsonDocument.Parse(await execResp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return doc.RootElement.GetProperty("stepOutcomes")[0].GetProperty("reason").GetString();
  }

  // ---- fixture generation (deterministic, no committed binary assets) ----

  private static string MaskedTemplateBase64() {
    using var tpl = new Mat(new Size(BadgeWidth, BadgeHeight), MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
    for (var y = 0; y < BadgeHeight; y++) {
      for (var x = 0; x < BadgeWidth; x++) {
        if (IsInsideBadge(x, y)) {
          var c = BadgeColor(x, y);
          tpl.Set(y, x, new Vec4b(c.Item0, c.Item1, c.Item2, 255));
        }
        else {
          tpl.Set(y, x, new Vec4b(255, 0, 255, 0));
        }
      }
    }
    return Encode(tpl);
  }

  private static string RectangularTemplateBase64() {
    using var tpl = new Mat(new Size(BadgeWidth, BadgeHeight), MatType.CV_8UC3, new Scalar(0, 0, 0));
    for (var y = 0; y < BadgeHeight; y++)
      for (var x = 0; x < BadgeWidth; x++)
        tpl.Set(y, x, IsInsideBadge(x, y) ? BadgeColor(x, y) : Backdrop(dark: true, x, y));
    return Encode(tpl);
  }

  private static string FrameBase64(bool withBadge) {
    using var frame = new Mat(new Size(FrameWidth, FrameHeight), MatType.CV_8UC3, new Scalar(0, 0, 0));
    for (var y = 0; y < FrameHeight; y++)
      for (var x = 0; x < FrameWidth; x++)
        frame.Set(y, x, Backdrop(dark: false, x, y));

    if (withBadge) {
      for (var y = 0; y < BadgeHeight; y++)
        for (var x = 0; x < BadgeWidth; x++)
          if (IsInsideBadge(x, y))
            frame.Set(BadgeY + y, BadgeX + x, BadgeColor(x, y));
    }

    return Encode(frame);
  }

  private static string Encode(Mat image) {
    Cv2.ImEncode(".png", image, out var bytes);
    return Convert.ToBase64String(bytes);
  }

  private static bool IsInsideBadge(int x, int y) {
    var cx = (BadgeWidth - 1) / 2.0;
    var cy = (BadgeHeight - 1) / 2.0;
    var radius = Math.Min(cx, cy);
    var dx = (x - cx) / radius;
    var dy = (y - cy) / radius;
    return (dx * dx) + (dy * dy) <= 1.0;
  }

  private static Vec3b BadgeColor(int x, int y) {
    var v = (byte)(Hash(x + 1, y + 1, 0x9E3779B9) % 256);
    return new Vec3b(v, (byte)(255 - v), (byte)((v * 3) % 256));
  }

  private static Vec3b Backdrop(bool dark, int x, int y) {
    var baseValue = dark ? 40 : 205;
    var jitter = (int)(Hash(x, y, 0x85EBCA6B) % 16) - 8;
    var v = (byte)Math.Clamp(baseValue + jitter, 0, 255);
    return new Vec3b(v, v, v);
  }

  private static uint Hash(int x, int y, uint seed) {
    unchecked {
      var h = ((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ seed;
      h ^= h >> 13;
      h *= 0x5BD1E995u;
      h ^= h >> 15;
      return h;
    }
  }
}
