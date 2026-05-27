using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class ConnectPrimitiveSessionReuseTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public ConnectPrimitiveSessionReuseTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task StartReusesSingleRunningContextForGameAndDevice() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var first = await client.PostAsJsonAsync(new Uri("/api/sessions/start", UriKind.Relative), new {
      primitiveAction = new {
        type = "connect-to-game",
        schemaVersion = "v1",
        payload = new {
          gameId = "reuse-game",
          adbSerial = "reuse-device"
        }
      }
    }).ConfigureAwait(false);
    first.StatusCode.Should().Be(HttpStatusCode.OK);
    var firstPayload = await first.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var firstId = firstPayload.GetProperty("sessionId").GetString();

    var second = await client.PostAsJsonAsync(new Uri("/api/sessions/start", UriKind.Relative), new {
      primitiveAction = new {
        type = "connect-to-game",
        schemaVersion = "v1",
        payload = new {
          gameId = "reuse-game",
          adbSerial = "reuse-device"
        }
      }
    }).ConfigureAwait(false);
    second.StatusCode.Should().Be(HttpStatusCode.OK);
    var secondPayload = await second.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var secondId = secondPayload.GetProperty("sessionId").GetString();

    secondId.Should().NotBe(firstId);

    var running = await client.GetAsync(new Uri("/api/sessions/running", UriKind.Relative)).ConfigureAwait(false);
    running.StatusCode.Should().Be(HttpStatusCode.OK);
    var runningPayload = await running.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sessions = runningPayload.GetProperty("sessions").EnumerateArray().ToArray();
    sessions.Should().HaveCount(1);
    sessions[0].GetProperty("sessionId").GetString().Should().Be(secondId);
  }
}
