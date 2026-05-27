using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class PrimitiveActionContractsTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string _dataDir;

  public PrimitiveActionContractsTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

    _dataDir = Path.Combine(Path.GetTempPath(), "GameBot", "contract-primitive-actions", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_dataDir);

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _dataDir);
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    try {
      if (Directory.Exists(_dataDir)) {
        Directory.Delete(_dataDir, recursive: true);
      }
    }
    catch {
      // Best effort cleanup for contract temp data.
    }
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task OpenApiContainsPrimitiveRoutesFromSnapshot() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var paths = doc.RootElement.GetProperty("paths");

    var snapshotPath = Path.Combine(AppContext.BaseDirectory, "ApiContractSnapshots", "primitive-actions-routes.snapshot.json");
    File.Exists(snapshotPath).Should().BeTrue();
    using var snapshotDoc = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath).ConfigureAwait(false));

    foreach (var route in snapshotDoc.RootElement.GetProperty("routes").EnumerateArray().Select(x => x.GetString())) {
      paths.TryGetProperty(route!, out _).Should().BeTrue($"expected route '{route}' in OpenAPI document");
    }

    foreach (var route in snapshotDoc.RootElement.GetProperty("removedRoutes").EnumerateArray().Select(x => x.GetString())) {
      paths.TryGetProperty(route!, out _).Should().BeFalse($"route '{route}' should stay removed");
    }
  }

  [Fact]
  public async Task CommandsAcceptInlinePrimitivePayload() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "contract-primitive-command",
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new {
            detectionTarget = new {
              referenceImageId = "home_button",
              confidence = 0.9,
              offsetX = 0,
              offsetY = 0
            }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var id = created.GetProperty("id").GetString();
    id.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var firstStep = fetched.GetProperty("steps").EnumerateArray().First();
    firstStep.GetProperty("type").GetString().Should().Be("PrimitiveTap");
    firstStep.GetProperty("primitiveTap").GetProperty("detectionTarget").GetProperty("referenceImageId").GetString().Should().Be("home_button");
  }

  [Fact]
  public async Task SequencesAcceptInlinePrimitivePayload() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "contract-primitive-sequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 10, y = 20 } }
        },
        new {
          stepId = "step-2",
          primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId = "child-command" } }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var id = created.GetProperty("id").GetString();
    id.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var steps = fetched.GetProperty("steps").EnumerateArray().ToArray();
    steps.Should().HaveCount(2);
    var firstType = GetStepPrimitiveType(steps[0]);
    var secondType = GetStepPrimitiveType(steps[1]);

    firstType.Should().Be("tap");
    secondType.Should().Be("command");
  }

  private static string? GetStepPrimitiveType(JsonElement step) {
    if (step.TryGetProperty("primitiveAction", out var primitive)) {
      if (primitive.TryGetProperty("type", out var directType)) {
        return directType.GetString();
      }
      if (primitive.TryGetProperty("primitiveAction", out var nested)
          && nested.TryGetProperty("type", out var nestedType)) {
        return nestedType.GetString();
      }
    }
    if (step.TryGetProperty("action", out var action)
        && action.TryGetProperty("type", out var actionType)) {
      return actionType.GetString();
    }
    return null;
  }
}
