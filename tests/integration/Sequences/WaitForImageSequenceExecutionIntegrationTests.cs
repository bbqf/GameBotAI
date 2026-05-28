using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

[Collection("ConfigIsolation")]
public sealed class WaitForImageSequenceExecutionIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string? _prevCaptureInterval;

  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public WaitForImageSequenceExecutionIntegrationTests() {
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
  public async Task ExecuteSequenceReturnsImageDetectedOutcomeAndContinues() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "wait-sequence-image", data = OneByOnePngBase64 });
    uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var sequenceId = await CreateSequenceAsync(client, new {
      timeoutMs = 40,
      detectionTarget = new {
        referenceImageId = "wait-sequence-image",
        confidence = 0.99
      }
    });

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Succeeded");
    var steps = execution.GetProperty("steps");
    steps.GetArrayLength().Should().Be(2);
    steps[0].GetProperty("actionOutcome").GetString().Should().Be("image_detected");
    steps[1].GetProperty("actionOutcome").GetString().Should().Be("executed");
  }

  [Fact]
  public async Task ExecuteSequenceWithoutConfiguredImageReturnsTimeoutOutcomeAndContinues() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var sequenceId = await CreateSequenceAsync(client, new {
      timeoutMs = 25
    });

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Succeeded");
    var steps = execution.GetProperty("steps");
    steps[0].GetProperty("actionOutcome").GetString().Should().Be("timeout_elapsed");
    steps[1].GetProperty("actionOutcome").GetString().Should().Be("executed");
  }

  [Fact]
  public async Task ExecuteSequenceWithMissingImageReturnsUnavailableOutcomeAndContinues() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var sequenceId = await CreateSequenceAsync(client, new {
      timeoutMs = 25,
      detectionTarget = new {
        referenceImageId = "missing-sequence-image",
        confidence = 0.99
      }
    });

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Succeeded");
    var steps = execution.GetProperty("steps");
    steps[0].GetProperty("actionOutcome").GetString().Should().Be("image_unavailable");
    steps[1].GetProperty("actionOutcome").GetString().Should().Be("executed");
  }

  private static async Task<string> CreateSequenceAsync(HttpClient client, object waitPayload) {
    var createPayload = new {
      name = "wait-execution-sequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "wait-step",
          primitiveAction = new {
            type = "WaitForImage",
            schemaVersion = "v1",
            payload = waitPayload
          }
        },
        new {
          stepId = "after-wait",
          primitiveAction = new {
            type = "command",
            schemaVersion = "v1",
            payload = new {
              commandId = "cmd-after-wait"
            }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return created.GetProperty("id").GetString()!;
  }
}