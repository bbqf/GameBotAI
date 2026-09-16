using System;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenCvSharp;
using Xunit;

namespace GameBot.ContractTests.Images;

/// <summary>
/// Feature 089 (issue #190): a transparency mask that retains almost nothing would match any patch
/// of screen. It is refused at upload, where the operator can still act on it.
/// </summary>
public sealed class UploadImageMaskValidationTests : IDisposable {
  private readonly string? _prevDynPort;
  private readonly string? _prevToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDataDir;
  private readonly string? _prevStorageRoot;
  private readonly string _dataDir;

  public UploadImageMaskValidationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevStorageRoot = Environment.GetEnvironmentVariable("Service__Storage__Root");

    _dataDir = Path.Combine(Path.GetTempPath(), $"gamebot-contract-maskval-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_dataDir);

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _dataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _dataDir);
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _prevStorageRoot);

    try {
      if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
    catch { }

    GC.SuppressFinalize(this);
  }

  /// <summary>A 16x16 BGRA PNG with <paramref name="opaquePixels"/> pixels left opaque.</summary>
  private static string MaskedPngBase64(int opaquePixels) {
    using var image = new Mat(new Size(16, 16), MatType.CV_8UC4, new Scalar(10, 200, 30, 0));
    var painted = 0;
    for (var y = 0; y < 16 && painted < opaquePixels; y++) {
      for (var x = 0; x < 16 && painted < opaquePixels; x++) {
        image.Set(y, x, new Vec4b((byte)(x * 13), (byte)(y * 7), 90, 255));
        painted++;
      }
    }
    Cv2.ImEncode(".png", image, out var bytes);
    return Convert.ToBase64String(bytes);
  }

  private static string OpaquePngBase64() {
    using var image = new Mat(new Size(16, 16), MatType.CV_8UC3, new Scalar(10, 200, 30));
    for (var y = 0; y < 16; y++)
      for (var x = 0; x < 16; x++)
        image.Set(y, x, new Vec3b((byte)(x * 13), (byte)(y * 7), 90));
    Cv2.ImEncode(".png", image, out var bytes);
    return Convert.ToBase64String(bytes);
  }

  private static HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  [Fact(DisplayName = "A fully transparent reference image is rejected")]
  public async Task FullyTransparentReferenceImageIsRejected() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative),
      new { id = "mask-empty", data = MaskedPngBase64(0) });

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_image");
  }

  [Fact(DisplayName = "A mask retaining fewer than the minimum pixels is rejected with the count in the hint")]
  public async Task MaskRetainingTooFewPixelsIsRejectedWithCountInHint() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative),
      new { id = "mask-tiny", data = MaskedPngBase64(3) });

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(body);
    var error = doc.RootElement.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("invalid_image");
    error.GetProperty("hint").GetString().Should().Contain("3 opaque pixels",
      "the operator needs to know how much of the image survived their erasing");
  }

  [Fact(DisplayName = "A mask retaining at least the minimum is accepted")]
  public async Task MaskRetainingAtLeastMinimumIsAccepted() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative),
      new { id = "mask-ok", data = MaskedPngBase64(64) });

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  [Fact(DisplayName = "An image with no transparency is accepted unchanged")]
  public async Task ImageWithNoTransparencyIsAcceptedUnchanged() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var resp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative),
      new { id = "no-alpha", data = OpaquePngBase64() });

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  [Fact(DisplayName = "Replacing an image with a degenerate mask is rejected too")]
  public async Task ReplacingImageWithDegenerateMaskIsRejected() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var created = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative),
      new { id = "mask-replace", data = OpaquePngBase64() });
    created.StatusCode.Should().Be(HttpStatusCode.Created);

    var replaced = await client.PutAsJsonAsync(new Uri("/api/images/mask-replace", UriKind.Relative),
      new { data = MaskedPngBase64(2) });

    replaced.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }
}
