using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

[Collection("ConfigIsolation")]
public sealed class WaitForImageAuthoringIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public WaitForImageAuthoringIntegrationTests() {
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
  public async Task CommandCreateReadUpdateSupportsWaitForImageRoundTrip() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "wait-authoring-command",
      steps = new[] {
        new {
          type = "WaitForImage",
          order = 0,
          waitForImage = new {
            detectionTarget = new {
              referenceImageId = "home_button",
              confidence = 0.8
            },
            timeoutMs = 1500
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var commandId = created.GetProperty("id").GetString();
    commandId.Should().NotBeNullOrWhiteSpace();
    created.GetProperty("steps")[0].GetProperty("waitForImage").GetProperty("timeoutMs").GetInt32().Should().Be(1500);

    var getResponse = await client.GetAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    fetched.GetProperty("steps")[0].GetProperty("waitForImage").GetProperty("detectionTarget").GetProperty("referenceImageId").GetString().Should().Be("home_button");

    var patchPayload = new {
      name = "wait-authoring-command-updated",
      steps = new[] {
        new {
          type = "WaitForImage",
          order = 0,
          waitForImage = new {
            detectionTarget = (object?)null,
            timeoutMs = 2500
          }
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative), patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var updatedResponse = await client.GetAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative)).ConfigureAwait(false);
    updatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    updated.GetProperty("name").GetString().Should().Be("wait-authoring-command-updated");
    var waitConfig = updated.GetProperty("steps")[0].GetProperty("waitForImage");
    waitConfig.GetProperty("timeoutMs").GetInt32().Should().Be(2500);
    waitConfig.TryGetProperty("detectionTarget", out var detectionTarget).Should().BeTrue();
    detectionTarget.ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task CreateCommandAcceptsZeroTimeoutForWaitForImage() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var commandReq = new {
      name = "ZeroTimeoutWait",
      steps = new[] {
        new {
          type = "WaitForImage",
          order = 0,
          waitForImage = new {
            timeoutMs = 0
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);

    var payload = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    payload.GetProperty("steps")[0].GetProperty("waitForImage").GetProperty("timeoutMs").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task CreateCommandRejectsNegativeTimeoutForWaitForImage() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var commandReq = new {
      name = "NegativeTimeoutWait",
      steps = new[] {
        new {
          type = "WaitForImage",
          order = 0,
          waitForImage = new {
            timeoutMs = -1
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    payload.Should().Contain("waitForImage.timeoutMs must be greater than or equal to zero");
  }
}
