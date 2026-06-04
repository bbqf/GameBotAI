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
public sealed class PrimitiveAuthoringFlowTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public PrimitiveAuthoringFlowTests() {
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
  public async Task CommandCreateReadUpdateSupportsInlinePrimitiveTap() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "primitive-authoring-command",
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new {
            detectionTarget = new {
              referenceImageId = "home_button",
              confidence = 0.8,
              offsetX = 1,
              offsetY = -1
            }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var commandId = created.GetProperty("id").GetString();
    commandId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var patchPayload = new {
      name = "primitive-authoring-command-updated",
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new {
            detectionTarget = new {
              referenceImageId = "alliance_button",
              confidence = 0.92,
              offsetX = 3,
              offsetY = 2
            }
          }
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative), patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var updatedResponse = await client.GetAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative)).ConfigureAwait(false);
    updatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    updated.GetProperty("name").GetString().Should().Be("primitive-authoring-command-updated");
    updated.GetProperty("steps")[0].GetProperty("primitiveTap").GetProperty("detectionTarget").GetProperty("referenceImageId").GetString().Should().Be("alliance_button");
  }

  [Fact]
  public async Task CommandCreateReadSupportsKeyInputStep() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "key-input-command",
      steps = new[] {
        new {
          type = "KeyInput",
          order = 0,
          keyInput = new { key = "Enter" }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var commandId = created.GetProperty("id").GetString();
    commandId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var retrieved = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var step = retrieved.GetProperty("steps")[0];
    step.GetProperty("type").GetString().Should().Be("KeyInput");
    step.GetProperty("keyInput").GetProperty("key").GetString().Should().Be("Enter");
  }

  [Fact]
  public async Task CommandCreateReadSupportsSwipeStep() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "swipe-command",
      steps = new[] {
        new {
          type = "Swipe",
          order = 0,
          swipe = new { startX = 100, startY = 800, endX = 100, endY = 200, durationMs = 300 }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var commandId = created.GetProperty("id").GetString();
    commandId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var retrieved = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var step = retrieved.GetProperty("steps")[0];
    step.GetProperty("type").GetString().Should().Be("Swipe");
    var swipe = step.GetProperty("swipe");
    swipe.GetProperty("startX").GetInt32().Should().Be(100);
    swipe.GetProperty("startY").GetInt32().Should().Be(800);
    swipe.GetProperty("endX").GetInt32().Should().Be(100);
    swipe.GetProperty("endY").GetInt32().Should().Be(200);
    swipe.GetProperty("durationMs").GetInt32().Should().Be(300);
  }

  [Fact]
  public async Task SequenceCreateReadUpdateSupportsInlinePrimitiveActions() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "primitive-authoring-sequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 120, y = 840 } }
        },
        new {
          stepId = "step-2",
          primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId = "nested-command" } }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var patchPayload = new {
      name = "primitive-authoring-sequence-updated",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 220, y = 940 } }
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative), patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var updatedResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    updatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    updated.GetProperty("name").GetString().Should().Be("primitive-authoring-sequence-updated");
    var updatedSteps = updated.GetProperty("steps").EnumerateArray().ToArray();
    updatedSteps.Should().HaveCount(1);
    var firstStep = updatedSteps[0];
    var firstType = firstStep.TryGetProperty("primitiveAction", out var primitiveAction)
      ? (primitiveAction.TryGetProperty("type", out var primitiveType)
        ? primitiveType.GetString()
        : primitiveAction.GetProperty("primitiveAction").GetProperty("type").GetString())
      : firstStep.GetProperty("action").GetProperty("type").GetString();
    firstType.Should().Be("tap");
  }
}
