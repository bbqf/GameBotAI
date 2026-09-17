#pragma warning disable CA2007, CA1861, CA1859, CA2000
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.IntegrationTests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenCvSharp;
using Xunit;

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 097 (issue #192): one reference image covers day and night renderings of the same art through
/// its alternates, managed over HTTP and honoured by detection without any sequence being edited.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class ImageAlternatesIntegrationTests : IDisposable {
  internal const int FrameWidth = 320;
  internal const int FrameHeight = 240;
  internal const int PatchSize = 40;
  internal const int PatchX = 150;
  internal const int PatchY = 100;
  internal const double Gate = 0.85;

  private readonly string? _prevUseAdb;
  private readonly string? _prevToken;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevScreen;
  private readonly string? _prevCaptureInterval;

  public ImageAlternatesIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevScreen = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");
    _prevCaptureInterval = Environment.GetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", "10");
    // The service's screen shows the "night" rendering of the art.
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", AlternatesFixtures.NightFrameBase64());
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

  // ---------------- US2: manage alternates ----------------

  [Fact(DisplayName = "Alternates: set, read back in order, replace, clear, and show in metadata")]
  public async Task AlternatesRoundTrip() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-n1", AlternatesFixtures.NightTemplateBase64());
    await UploadAsync(client, "anchor-n2", AlternatesFixtures.DimNightTemplateBase64());

    var put = await PutAlternatesAsync(client, "anchor", "anchor-n2", "anchor-n1");
    put.StatusCode.Should().Be(HttpStatusCode.OK);

    using (var doc = await GetAlternatesAsync(client, "anchor")) {
      AlternateIds(doc).Should().Equal("anchor-n2", "anchor-n1");
      doc.RootElement.GetProperty("alternates").EnumerateArray().Should().OnlyContain(e => e.GetProperty("exists").GetBoolean());
    }

    using (var meta = await GetJsonAsync(client, "/api/images/anchor/metadata")) {
      meta.RootElement.GetProperty("alternates").EnumerateArray().Select(e => e.GetString()).Should().Equal("anchor-n2", "anchor-n1");
    }

    (await PutAlternatesAsync(client, "anchor", "anchor-n1")).StatusCode.Should().Be(HttpStatusCode.OK);
    using (var doc = await GetAlternatesAsync(client, "anchor")) {
      AlternateIds(doc).Should().Equal("anchor-n1");
    }

    (await PutAlternatesAsync(client, "anchor")).StatusCode.Should().Be(HttpStatusCode.OK);
    using (var doc = await GetAlternatesAsync(client, "anchor")) {
      AlternateIds(doc).Should().BeEmpty();
    }
    using (var meta = await GetJsonAsync(client, "/api/images/anchor/metadata")) {
      meta.RootElement.GetProperty("alternates").GetArrayLength().Should().Be(0);
    }
  }

  [Fact(DisplayName = "Alternates: unknown primary is 404 on GET and PUT")]
  public async Task UnknownPrimaryIsNotFound() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    (await client.GetAsync(new Uri("/api/images/nope/alternates", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await PutAlternatesAsync(client, "nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact(DisplayName = "Alternates: invalid requests are refused and leave the stored list unchanged")]
  public async Task InvalidRequestsAreRefused() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-n1", AlternatesFixtures.NightTemplateBase64());
    for (var i = 0; i < 9; i++) await UploadAsync(client, $"extra-{i}", AlternatesFixtures.DimNightTemplateBase64());
    (await PutAlternatesAsync(client, "anchor", "anchor-n1")).StatusCode.Should().Be(HttpStatusCode.OK);

    var missingBody = await client.PutAsJsonAsync(new Uri("/api/images/anchor/alternates", UriKind.Relative), new { });
    missingBody.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ErrorCodeAsync(missingBody)).Should().Be("invalid_request");

    await AssertRefusedAsync(client, new[] { "extra-0", "extra-1", "extra-2", "extra-3", "extra-4", "extra-5", "extra-6", "extra-7", "extra-8" }, expectedIds: Array.Empty<string>(), messageContains: "8");
    await AssertRefusedAsync(client, new[] { "anchor" }, expectedIds: new[] { "anchor" });
    await AssertRefusedAsync(client, new[] { "extra-0", "EXTRA-0" }, expectedIds: new[] { "EXTRA-0" });
    await AssertRefusedAsync(client, new[] { "ghost" }, expectedIds: new[] { "ghost" });
    await AssertRefusedAsync(client, new[] { "bad id!" }, expectedIds: new[] { "bad id!" });

    using var doc = await GetAlternatesAsync(client, "anchor");
    AlternateIds(doc).Should().Equal("anchor-n1");
  }

  // ---------------- US1: detection honours alternates ----------------

  [Fact(DisplayName = "Detect: a night-only alternate turns no-match into a match for the named image")]
  public async Task NightAlternateMakesPrimaryMatch() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());

    using (var before = await DetectAsync(client, "anchor")) {
      before.RootElement.GetProperty("matches").GetArrayLength().Should().Be(0, "the day crop does not match the night screen");
    }

    (await PutAlternatesAsync(client, "anchor", "anchor-night")).StatusCode.Should().Be(HttpStatusCode.OK);

    using var after = await DetectAsync(client, "anchor");
    var matches = after.RootElement.GetProperty("matches");
    matches.GetArrayLength().Should().Be(1);
    matches[0].GetProperty("templateId").GetString().Should().Be("anchor");
    matches[0].GetProperty("matchedReferenceId").GetString().Should().Be("anchor-night");
    matches[0].GetProperty("confidence").GetDouble().Should().BeGreaterThanOrEqualTo(Gate);
  }

  [Fact(DisplayName = "Detect: two references at the same place yield one match from the higher score")]
  public async Task SamePlaceYieldsOneMatchFromHigherScore() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DimNightTemplateBase64());
    await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
    (await PutAlternatesAsync(client, "anchor", "anchor-night")).StatusCode.Should().Be(HttpStatusCode.OK);

    using var doc = await DetectAsync(client, "anchor", maxResults: 5);
    var matches = doc.RootElement.GetProperty("matches");
    matches.GetArrayLength().Should().Be(1);
    matches[0].GetProperty("matchedReferenceId").GetString().Should().Be("anchor-night", "the exact night crop outscores the perturbed one");
  }

  [Fact(DisplayName = "Detect: no reference visible yields no match")]
  public async Task NoReferenceVisibleYieldsNoMatch() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "other", AlternatesFixtures.OtherTemplateBase64());
    (await PutAlternatesAsync(client, "anchor", "other")).StatusCode.Should().Be(HttpStatusCode.OK);

    using var doc = await DetectAsync(client, "anchor");
    doc.RootElement.GetProperty("matches").GetArrayLength().Should().Be(0);
  }

  [Fact(DisplayName = "Detect: an image without alternates reports exactly what it did before")]
  public async Task ImageWithoutAlternatesIsUnchanged() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "plain", AlternatesFixtures.DimNightTemplateBase64());
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());

    using var baseline = await DetectAsync(client, "plain", threshold: 0.0, maxResults: 5);
    (await PutAlternatesAsync(client, "anchor", "anchor-night")).StatusCode.Should().Be(HttpStatusCode.OK);
    using var afterwards = await DetectAsync(client, "plain", threshold: 0.0, maxResults: 5);

    var a = baseline.RootElement.GetProperty("matches").EnumerateArray().ToList();
    var b = afterwards.RootElement.GetProperty("matches").EnumerateArray().ToList();
    b.Should().HaveCount(a.Count);
    for (var i = 0; i < a.Count; i++) {
      b[i].GetProperty("confidence").GetDouble().Should().Be(a[i].GetProperty("confidence").GetDouble());
      b[i].GetProperty("bbox").GetRawText().Should().Be(a[i].GetProperty("bbox").GetRawText());
      b[i].GetProperty("matchedReferenceId").GetString().Should().Be("plain");
    }
  }

  [Fact(DisplayName = "Detect: a deleted alternate is skipped, not an error")]
  public async Task DeletedAlternateIsSkipped() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
    await UploadAsync(client, "anchor-dim", AlternatesFixtures.DimNightTemplateBase64());
    (await PutAlternatesAsync(client, "anchor", "anchor-night", "anchor-dim")).StatusCode.Should().Be(HttpStatusCode.OK);

    (await client.DeleteAsync(new Uri("/api/images/anchor-night", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);

    using var doc = await DetectAsync(client, "anchor");
    var matches = doc.RootElement.GetProperty("matches");
    matches.GetArrayLength().Should().Be(1);
    matches[0].GetProperty("matchedReferenceId").GetString().Should().Be("anchor-dim");

    using var alts = await GetAlternatesAsync(client, "anchor");
    var entries = alts.RootElement.GetProperty("alternates").EnumerateArray().ToList();
    entries.Select(e => e.GetProperty("id").GetString()).Should().Equal("anchor-night", "anchor-dim");
    entries[0].GetProperty("exists").GetBoolean().Should().BeFalse();
    entries[1].GetProperty("exists").GetBoolean().Should().BeTrue();
  }

  [Fact(DisplayName = "Detect-all still reports an alternate under its own id")]
  public async Task DetectAllReportsAlternatesUnderTheirOwnIds() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
    (await PutAlternatesAsync(client, "anchor", "anchor-night")).StatusCode.Should().Be(HttpStatusCode.OK);

    var captureResp = await client.PostAsJsonAsync(new Uri("/api/images/capture", UriKind.Relative), new { });
    if (captureResp.StatusCode != HttpStatusCode.OK && captureResp.StatusCode != HttpStatusCode.Created) {
      // Capture is unavailable in this configuration; nothing to assert.
      return;
    }
    using var captureDoc = JsonDocument.Parse(await captureResp.Content.ReadAsStringAsync());
    var captureId = captureDoc.RootElement.GetProperty("captureId").GetString();

    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect-all", UriKind.Relative), new { captureId });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    var ids = doc.RootElement.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("imageId").GetString()).ToList();
    ids.Should().Contain("anchor-night");
    ids.Should().NotContain("anchor", "detect-all does not merge alternates into their primary");
  }

  // ---------------- US3: lifecycle ----------------

  [Fact(DisplayName = "Lifecycle: alternates survive a restart and an overwrite of the primary")]
  public async Task AlternatesSurviveRestartAndOverwrite() {
    using (var app = new WebApplicationFactory<Program>()) {
      using var client = CreateClient(app);
      await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
      await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
      (await PutAlternatesAsync(client, "anchor", "anchor-night")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    using (var app = new WebApplicationFactory<Program>()) {
      using var client = CreateClient(app);
      using (var alts = await GetAlternatesAsync(client, "anchor")) {
        AlternateIds(alts).Should().Equal("anchor-night");
      }

      var overwrite = await client.PutAsJsonAsync(new Uri("/api/images/anchor", UriKind.Relative), new { data = AlternatesFixtures.OtherTemplateBase64() });
      overwrite.StatusCode.Should().Be(HttpStatusCode.OK);

      using var detect = await DetectAsync(client, "anchor");
      detect.RootElement.GetProperty("matches")[0].GetProperty("matchedReferenceId").GetString().Should().Be("anchor-night");
    }
  }

  [Fact(DisplayName = "Lifecycle: deleting the primary deletes its alternates list")]
  public async Task DeletingPrimaryDeletesItsList() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
    (await PutAlternatesAsync(client, "anchor", "anchor-night")).StatusCode.Should().Be(HttpStatusCode.OK);

    (await client.DeleteAsync(new Uri("/api/images/anchor", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());

    using var alts = await GetAlternatesAsync(client, "anchor");
    AlternateIds(alts).Should().BeEmpty();
  }

  // ---------------- helpers ----------------

  internal static HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  internal static async Task UploadAsync(HttpClient client, string id, string base64) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id, data = base64 });
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  internal static Task<HttpResponseMessage> PutAlternatesAsync(HttpClient client, string id, params string[] alternates) =>
    client.PutAsJsonAsync(new Uri($"/api/images/{id}/alternates", UriKind.Relative), new { alternates });

  private static async Task<JsonDocument> GetAlternatesAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/images/{id}/alternates", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
  }

  private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path) {
    var resp = await client.GetAsync(new Uri(path, UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
  }

  private static List<string?> AlternateIds(JsonDocument doc) =>
    doc.RootElement.GetProperty("alternates").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

  private static async Task<string?> ErrorCodeAsync(HttpResponseMessage resp) {
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("error").GetProperty("code").GetString();
  }

  private static async Task AssertRefusedAsync(HttpClient client, string[] alternates, string[] expectedIds, string? messageContains = null) {
    var resp = await PutAlternatesAsync(client, "anchor", alternates);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    var error = doc.RootElement.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("invalid_alternates");
    error.GetProperty("ids").EnumerateArray().Select(e => e.GetString()).Should().Equal(expectedIds);
    if (messageContains is not null) error.GetProperty("message").GetString().Should().Contain(messageContains);
  }

  internal static async Task<JsonDocument> DetectAsync(HttpClient client, string id, double threshold = Gate, int maxResults = 1) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = id, threshold, maxResults });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
  }
}

/// <summary>Deterministic synthetic art for the alternates tests; no committed binary assets.</summary>
internal static class AlternatesFixtures {
  private const int Size = ImageAlternatesIntegrationTests.PatchSize;

  public static string NightFrameBase64() {
    using var frame = new Mat(new Size(ImageAlternatesIntegrationTests.FrameWidth, ImageAlternatesIntegrationTests.FrameHeight), MatType.CV_8UC3, new Scalar(0, 0, 0));
    for (var y = 0; y < frame.Rows; y++)
      for (var x = 0; x < frame.Cols; x++) {
        var v = (byte)(96 + (Hash(x, y, 0x1234567u) % 24));
        frame.Set(y, x, new Vec3b(v, v, v));
      }
    for (var y = 0; y < Size; y++)
      for (var x = 0; x < Size; x++)
        frame.Set(ImageAlternatesIntegrationTests.PatchY + y, ImageAlternatesIntegrationTests.PatchX + x, Texture(x, y, 0xA5A5A5u));
    return Encode(frame);
  }

  /// <summary>The same art as the screen shows, exactly (the "night" crop).</summary>
  public static string NightTemplateBase64() => TextureBase64(0xA5A5A5u, perturb: false);

  /// <summary>The night art with added noise: still above the gate, but below the exact crop.</summary>
  public static string DimNightTemplateBase64() => TextureBase64(0xA5A5A5u, perturb: true);

  /// <summary>Unrelated art standing in for the daylight rendering that the night screen does not match.</summary>
  public static string DayTemplateBase64() => TextureBase64(0x5A5A5Au, perturb: false);

  public static string OtherTemplateBase64() => TextureBase64(0x777777u, perturb: false);

  private static string TextureBase64(uint seed, bool perturb) {
    using var tpl = new Mat(new Size(Size, Size), MatType.CV_8UC3, new Scalar(0, 0, 0));
    for (var y = 0; y < Size; y++)
      for (var x = 0; x < Size; x++) {
        var c = Texture(x, y, seed);
        if (perturb) {
          var n = (int)(Hash(x, y, 0xBEEFu) % 50) - 25;
          c = new Vec3b(Clamp(c.Item0 + n), Clamp(c.Item1 + n), Clamp(c.Item2 + n));
        }
        tpl.Set(y, x, c);
      }
    return Encode(tpl);
  }

  private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

  private static Vec3b Texture(int x, int y, uint seed) {
    var v = (byte)(Hash(x + 1, y + 1, seed) % 256);
    return new Vec3b(v, (byte)(255 - v), (byte)((v * 7) % 256));
  }

  private static string Encode(Mat image) {
    Cv2.ImEncode(".png", image, out var bytes);
    return Convert.ToBase64String(bytes);
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
