using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

[Collection("ConfigIsolation")]
public sealed class StepsEndpointsTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public StepsEndpointsTests() {
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
  public async Task ExecuteStepRejectsPrimitiveTapWithMissingReferenceImageId() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var req = new {
      step = new {
        type = "PrimitiveTap",
        order = 0,
        primitiveTap = new { detectionTarget = new { referenceImageId = "" } }
      }
    };

    var response = await client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), req);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var payload = await response.Content.ReadAsStringAsync();
    payload.Should().Contain("referenceImageId");
  }

  [Fact]
  public async Task ExecuteStepRejectsCommandTypeStep() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var req = new {
      step = new {
        type = "Command",
        order = 0,
        targetId = "some-command-id"
      }
    };

    var response = await client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), req);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var payload = await response.Content.ReadAsStringAsync();
    payload.Should().Contain("Command");
  }

  [Fact]
  public async Task ExecuteStepReturnsMissingSessionWhenNoSessionRunning() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var req = new {
      step = new {
        type = "KeyInput",
        order = 0,
        keyInput = new { key = "ENTER" }
      }
    };

    var response = await client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), req);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var payload = await response.Content.ReadAsStringAsync();
    payload.Should().Contain("session");
  }
}
