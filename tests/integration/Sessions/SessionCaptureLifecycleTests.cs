using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Emulator.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sessions;

/// <summary>
/// Feature 166 (FR-013): the capture loop of a session follows the session lifecycle. The create of a
/// session starts the loop, and the delete of the session stops it. Stub mode starts no loop.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SessionCaptureLifecycleTests {
  [Fact]
  public async Task DeleteSessionStopsCaptureLoop() {
    using var host = new WaitForImageCaptureTestHost();
    host.Provider.SetFrame(host.ImageFramePng);
    var sessionId = await host.CreateSessionAsync().ConfigureAwait(true);

    var firstFrame = await host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);
    firstFrame.Should().BeTrue("POST /api/sessions starts the capture loop of a session with a device");

    var resp = await host.Client.DeleteAsync(new Uri($"/api/sessions/{sessionId}", UriKind.Relative)).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    host.CaptureService.GetCachedFrame(sessionId).Should().BeNull();
    host.CaptureService.GetCaptureMetrics(sessionId).Should().BeNull();
  }

  [Fact]
  public async Task StubModeStartsNoCaptureLoop() {
    var previous = new Dictionary<string, string?> {
      ["GAMEBOT_USE_ADB"] = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB"),
      ["GAMEBOT_DYNAMIC_PORT"] = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT"),
      ["GAMEBOT_AUTH_TOKEN"] = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN"),
      ["GAMEBOT_DATA_DIR"] = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR"),
    };
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    try {
      using var app = new WebApplicationFactory<Program>();
      using var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

      var create = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = "game-166-stub" }).ConfigureAwait(true);
      create.StatusCode.Should().Be(HttpStatusCode.Created);
      using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync().ConfigureAwait(true));
      var sessionId = created.RootElement.GetProperty("id").GetString();

      app.Services.GetService<BackgroundScreenCaptureService>().Should().BeNull();

      var running = await client.GetAsync(new Uri("/api/sessions/running", UriKind.Relative)).ConfigureAwait(true);
      running.EnsureSuccessStatusCode();
      using var doc = JsonDocument.Parse(await running.Content.ReadAsStringAsync().ConfigureAwait(true));
      foreach (var session in doc.RootElement.GetProperty("sessions").EnumerateArray()) {
        if (session.GetProperty("sessionId").GetString() != sessionId) continue;
        if (session.TryGetProperty("captureRateFps", out var fps)) {
          fps.ValueKind.Should().Be(JsonValueKind.Null);
        }
      }
    }
    finally {
      foreach (var (name, value) in previous) Environment.SetEnvironmentVariable(name, value);
    }
  }
}
