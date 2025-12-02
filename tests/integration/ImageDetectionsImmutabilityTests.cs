using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class ImageDetectionsImmutabilityTests {
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public ImageDetectionsImmutabilityTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
  }

  [Fact]
  public async Task DetectDoesNotMutateStoredImage() {
    // Prepare isolated data dir and remember it for reading file back
    var dataDir = TestEnvironment.PrepareCleanDataDir();

    // Provide screenshot so detect path runs
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var imgId = "immut";
    var upload = await client.PostAsJsonAsync(new Uri("/images", UriKind.Relative), new { id = imgId, data = OneByOnePngBase64 });
    upload.StatusCode.Should().Be(HttpStatusCode.Created);

    var path = Path.Combine(dataDir, "images", imgId + ".png");
    File.Exists(path).Should().BeTrue();

    static byte[] Hash(string p) {
      using var sha = SHA256.Create();
      using var fs = File.OpenRead(p);
      return sha.ComputeHash(fs);
    }

    var before = Hash(path);

    // Call detect (should not alter stored file)
    var detect = await client.PostAsJsonAsync(new Uri("/images/detect", UriKind.Relative), new { referenceImageId = imgId });
    detect.StatusCode.Should().Be(HttpStatusCode.OK);

    var after = Hash(path);
    before.Should().BeEquivalentTo(after);
  }
}
