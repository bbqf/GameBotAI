using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class ConnectPrimitiveSessionStartTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public ConnectPrimitiveSessionStartTests() {
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
  public async Task StartRejectsMissingConnectPrimitiveFields() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var response = await client.PostAsJsonAsync(new Uri("/api/sessions/start", UriKind.Relative), new {
      primitiveAction = new {
        type = "connect-to-game",
        schemaVersion = "v1",
        payload = new {
          gameId = "game-1"
        }
      }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    body.Should().Contain("primitiveAction.payload.gameId and primitiveAction.payload.adbSerial are required");
  }

  [Fact]
  public async Task StartCreatesRunningSessionFromConnectPrimitivePayload() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var response = await client.PostAsJsonAsync(new Uri("/api/sessions/start", UriKind.Relative), new {
      primitiveAction = new {
        type = "connect-to-game",
        schemaVersion = "v1",
        payload = new {
          gameId = "game-2",
          adbSerial = "emu-2"
        }
      }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var payload = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    payload.GetProperty("sessionId").GetString().Should().NotBeNullOrWhiteSpace();
    payload.GetProperty("runningSessions").EnumerateArray().Should().NotBeEmpty();
  }
}
