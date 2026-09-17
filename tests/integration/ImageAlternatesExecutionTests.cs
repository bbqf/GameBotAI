#pragma warning disable CA2007, CA1861, CA1859, CA2000
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.IntegrationTests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static GameBot.IntegrationTests.ImageAlternatesIntegrationTests;

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 097 (issue #192): stock command steps naming a reference image are satisfied through its
/// night alternate, with no change to the step definitions.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class ImageAlternatesExecutionTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevToken;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevScreen;
  private readonly string? _prevCaptureInterval;

  public ImageAlternatesExecutionTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevScreen = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");
    _prevCaptureInterval = Environment.GetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", "10");
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

  [Fact(DisplayName = "Wait-for-image: an unmodified step is satisfied through the night alternate")]
  public async Task WaitForImageIsSatisfiedThroughAlternate() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
    var sessionId = await CreateSessionAsync(client, "AlternatesWaitGame");

    var before = await RunAsync(client, sessionId, WaitForImageStep("anchor"), "wait-before");
    before.GetProperty("reason").GetString().Should().NotBe("image_detected");

    (await PutAlternatesAsync(client, "anchor", "anchor-night")).StatusCode.Should().Be(HttpStatusCode.OK);

    var after = await RunAsync(client, sessionId, WaitForImageStep("anchor"), "wait-after");
    after.GetProperty("reason").GetString().Should().Be("image_detected");
  }

  [Fact(DisplayName = "Primitive tap: an unmodified image-anchored tap resolves through the night alternate")]
  public async Task PrimitiveTapResolvesThroughAlternate() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    await UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
    (await PutAlternatesAsync(client, "anchor", "anchor-night")).StatusCode.Should().Be(HttpStatusCode.OK);
    var sessionId = await CreateSessionAsync(client, "AlternatesTapGame");

    var outcome = await RunAsync(client, sessionId, PrimitiveTapStep("anchor"), "tap");

    outcome.GetProperty("status").GetString().Should().Be("executed");
    var point = outcome.GetProperty("resolvedPoint");
    point.GetProperty("x").GetInt32().Should().Be(PatchX + (PatchSize / 2));
    point.GetProperty("y").GetInt32().Should().Be(PatchY + (PatchSize / 2));
  }

  private static object WaitForImageStep(string referenceImageId) => new {
    type = "WaitForImage",
    order = 0,
    waitForImage = new {
      timeoutMs = 200,
      detectionTarget = new { referenceImageId, confidence = Gate, offsetX = 0, offsetY = 0, selectionStrategy = "HighestConfidence" }
    }
  };

  private static object PrimitiveTapStep(string referenceImageId) => new {
    type = "PrimitiveTap",
    order = 0,
    primitiveTap = new {
      detectionTarget = new { referenceImageId, confidence = Gate, offsetX = 0, offsetY = 0, selectionStrategy = "HighestConfidence" }
    }
  };

  private static async Task<string> CreateSessionAsync(HttpClient client, string gameName) {
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = gameName, description = "desc" });
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = game!["id"]!.ToString() });
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    return session!["id"]!.ToString()!;
  }

  /// <summary>Creates a one-step command, force-executes it and returns its step outcome.</summary>
  private static async Task<JsonElement> RunAsync(HttpClient client, string sessionId, object step, string name) {
    var createResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name, steps = new[] { step } });
    createResp.EnsureSuccessStatusCode();
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var commandId = created!["id"]!.ToString()!;

    var execResp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null);
    execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    using var doc = JsonDocument.Parse(await execResp.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("stepOutcomes")[0].Clone();
  }
}
