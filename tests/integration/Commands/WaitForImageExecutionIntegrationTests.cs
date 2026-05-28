using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

[Collection("ConfigIsolation")]
public sealed class WaitForImageExecutionIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string? _prevCaptureInterval;

  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public WaitForImageExecutionIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevCaptureInterval = Environment.GetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", "10");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", _prevCaptureInterval);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", null);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task ForceExecuteWaitForImageReturnsDetectedOutcomeAndContinuesToNextStep() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    await UploadImageAsync(client, "wait-image").ConfigureAwait(false);
    await UploadImageAsync(client, "tap-image").ConfigureAwait(false);
    var sessionId = await CreateSessionAsync(client, "WaitDetectGame").ConfigureAwait(false);
    var commandId = await CreateWaitThenTapCommandAsync(client, new {
      timeoutMs = 40,
      detectionTarget = new {
        referenceImageId = "wait-image",
        confidence = 0.99,
        offsetX = 0,
        offsetY = 0,
        selectionStrategy = "HighestConfidence"
      }
    }).ConfigureAwait(false);

    var execResp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null).ConfigureAwait(false);
    execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = await JsonDocument.ParseAsync(await execResp.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
    doc.RootElement.GetProperty("accepted").GetInt32().Should().Be(1);
    var outcomes = doc.RootElement.GetProperty("stepOutcomes");
    outcomes.GetArrayLength().Should().Be(2);

    outcomes[0].GetProperty("stepType").GetString().Should().Be("waitForImage");
    outcomes[0].GetProperty("status").GetString().Should().Be("executed");
    outcomes[0].GetProperty("reason").GetString().Should().Be("image_detected");
    outcomes[0].GetProperty("effectiveTimeoutMs").GetInt32().Should().Be(40);
    outcomes[0].GetProperty("referenceImageId").GetString().Should().Be("wait-image");
    outcomes[0].GetProperty("imageLoadStatus").GetString().Should().Be("loaded");

    outcomes[1].GetProperty("stepType").GetString().Should().Be("primitiveTap");
    outcomes[1].GetProperty("status").GetString().Should().Be("executed");
  }

  [Fact]
  public async Task ForceExecuteWaitForImageWithoutConfiguredImageTimesOutAndContinues() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    await UploadImageAsync(client, "tap-image").ConfigureAwait(false);
    var sessionId = await CreateSessionAsync(client, "WaitNoImageGame").ConfigureAwait(false);
    var commandId = await CreateWaitThenTapCommandAsync(client, new {
      timeoutMs = 25
    }).ConfigureAwait(false);

    var execResp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null).ConfigureAwait(false);
    execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = await JsonDocument.ParseAsync(await execResp.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
    doc.RootElement.GetProperty("accepted").GetInt32().Should().Be(1);
    var outcomes = doc.RootElement.GetProperty("stepOutcomes");
    outcomes.GetArrayLength().Should().Be(2);

    outcomes[0].GetProperty("stepType").GetString().Should().Be("waitForImage");
    outcomes[0].GetProperty("status").GetString().Should().Be("completed_timeout");
    outcomes[0].GetProperty("reason").GetString().Should().Be("timeout_elapsed");
    outcomes[0].GetProperty("effectiveTimeoutMs").GetInt32().Should().Be(25);
    if (outcomes[0].TryGetProperty("referenceImageId", out var referenceImageId)) {
      referenceImageId.ValueKind.Should().Be(JsonValueKind.Null);
    }

    outcomes[1].GetProperty("status").GetString().Should().Be("executed");
  }

  [Fact]
  public async Task ForceExecuteWaitForImageWithMissingImageReturnsUnavailableOutcomeAndContinues() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    await UploadImageAsync(client, "tap-image").ConfigureAwait(false);
    var sessionId = await CreateSessionAsync(client, "WaitMissingImageGame").ConfigureAwait(false);
    var commandId = await CreateWaitThenTapCommandAsync(client, new {
      timeoutMs = 25,
      detectionTarget = new {
        referenceImageId = "missing-image",
        confidence = 0.99,
        offsetX = 0,
        offsetY = 0,
        selectionStrategy = "HighestConfidence"
      }
    }).ConfigureAwait(false);

    var execResp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null).ConfigureAwait(false);
    execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = await JsonDocument.ParseAsync(await execResp.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
    doc.RootElement.GetProperty("accepted").GetInt32().Should().Be(1);
    var outcomes = doc.RootElement.GetProperty("stepOutcomes");
    outcomes.GetArrayLength().Should().Be(2);

    outcomes[0].GetProperty("stepType").GetString().Should().Be("waitForImage");
    outcomes[0].GetProperty("status").GetString().Should().Be("completed_image_unavailable");
    outcomes[0].GetProperty("reason").GetString().Should().Be("image_unavailable");
    outcomes[0].GetProperty("effectiveTimeoutMs").GetInt32().Should().Be(25);
    outcomes[0].GetProperty("referenceImageId").GetString().Should().Be("missing-image");
    outcomes[0].GetProperty("imageLoadStatus").GetString().Should().Be("missing");

    outcomes[1].GetProperty("status").GetString().Should().Be("executed");
  }

  private static async Task UploadImageAsync(HttpClient client, string imageId) {
    var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = imageId, data = OneByOnePngBase64 }).ConfigureAwait(false);
    uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);
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

  private static async Task<string> CreateWaitThenTapCommandAsync(HttpClient client, object waitForImage) {
    var commandReq = new {
      name = "WaitThenTap",
      triggerId = (string?)null,
      steps = new object[] {
        new {
          type = "WaitForImage",
          order = 0,
          waitForImage
        },
        new {
          type = "PrimitiveTap",
          order = 1,
          primitiveTap = new {
            detectionTarget = new {
              referenceImageId = "tap-image",
              confidence = 0.99,
              offsetX = 0,
              offsetY = 0,
              selectionStrategy = "HighestConfidence"
            }
          }
        }
      }
    };

    var commandResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq).ConfigureAwait(false);
    commandResp.EnsureSuccessStatusCode();
    var command = await commandResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    return command!["id"]!.ToString()!;
  }
}
