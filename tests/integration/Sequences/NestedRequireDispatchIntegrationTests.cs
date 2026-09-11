using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Integration tests for bug B-005: a step nested inside a Loop or If body marked
/// <c>requireDispatch: true</c> must fail the step (and the run) when its action does not
/// dispatch, exactly as a top-level step already does — at any nesting depth. Before the fix,
/// <c>SequencesEndpoints.MapBodySteps</c> silently dropped the flag for every nested step.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class NestedRequireDispatchIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string? _prevScreenImage;

  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public NestedRequireDispatchIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevScreenImage = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", _prevScreenImage);
    GC.SuppressFinalize(this);
  }

  // A PrimitiveTap step whose detection target is never uploaded fails detection immediately
  // (no retries needed), which SequenceExecutionService.ClassifyDispatch reports as Dispatched=false.
  private static async Task<string> CreateNonDispatchingCommandAsync(HttpClient client, string name) {
    var commandReq = new {
      name,
      triggerId = (string?)null,
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new {
            detectionTarget = new { referenceImageId = "does_not_exist", confidence = 0.99, offsetX = 0, offsetY = 0 }
          }
        }
      }
    };
    var resp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    var created = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    return created!["id"]!.ToString()!;
  }

  // A GoToHomeScreen step is a plain key input with no image detection involved at all — in stub
  // mode (GAMEBOT_USE_ADB=false) it is always accepted, which ClassifyDispatch reports as
  // Dispatched=true.
  private static async Task<string> CreateDispatchingCommandAsync(HttpClient client, string name) {
    var commandReq = new {
      name,
      triggerId = (string?)null,
      steps = new[] {
        new { type = "GoToHomeScreen", order = 0 }
      }
    };
    var resp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    var created = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    return created!["id"]!.ToString()!;
  }

  private static object CommandStepPayload(string stepId, string commandId, bool requireDispatch) => new {
    stepId,
    stepType = "Action",
    requireDispatch,
    primitiveAction = new {
      type = "command",
      schemaVersion = "v1",
      payload = new { commandId }
    }
  };

  // "command"-typed steps dispatch via CommandExecutor.ForceExecuteDetailedAsync, which (unlike
  // primitive tap/swipe/key steps) requires a real session — there is no ambient fallback.
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

  private static async Task<JsonElement> CreateAndExecuteAsync(HttpClient client, string name, object[] steps) {
    var sessionId = await CreateSessionAsync(client, name + "-game").ConfigureAwait(false);

    var createPayload = new { name, version = 1, steps };
    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { sessionId }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    return await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
  }

  [Fact]
  public async Task RequireDispatchStepNestedInLoopBodyFailsTheRunWhenItDoesNotDispatch() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var missCommandId = await CreateNonDispatchingCommandAsync(client, "loop-miss-cmd").ConfigureAwait(false);

    var steps = new object[] {
      new {
        stepId = "loop-1",
        stepType = "Loop",
        loop = new { loopType = "count", count = 1, maxIterations = 1 },
        body = new object[] { CommandStepPayload("tap-1", missCommandId, requireDispatch: true) }
      }
    };

    var execution = await CreateAndExecuteAsync(client, "loop-require-dispatch-miss", steps).ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Failed");
  }

  [Fact]
  public async Task RequireDispatchStepNestedInIfThenBranchFailsTheRunWhenItDoesNotDispatch() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var okCommandId = await CreateDispatchingCommandAsync(client, "if-then-gate-cmd").ConfigureAwait(false);
    var missCommandId = await CreateNonDispatchingCommandAsync(client, "if-then-miss-cmd").ConfigureAwait(false);

    var steps = new object[] {
      CommandStepPayload("gate", okCommandId, requireDispatch: false),
      new {
        stepId = "if-1",
        stepType = "If",
        @if = new { condition = new { type = "commandOutcome", stepRef = "gate", expectedState = "success" } },
        body = new object[] { CommandStepPayload("tap-1", missCommandId, requireDispatch: true) }
      }
    };

    var execution = await CreateAndExecuteAsync(client, "if-then-require-dispatch-miss", steps).ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Failed");
  }

  [Fact]
  public async Task RequireDispatchStepNestedInIfElseBranchFailsTheRunWhenItDoesNotDispatch() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var okCommandId = await CreateDispatchingCommandAsync(client, "if-else-gate-cmd").ConfigureAwait(false);
    var missCommandId = await CreateNonDispatchingCommandAsync(client, "if-else-miss-cmd").ConfigureAwait(false);

    var steps = new object[] {
      CommandStepPayload("gate", okCommandId, requireDispatch: false),
      new {
        stepId = "if-1",
        stepType = "If",
        // gate succeeded, so "expectedState: failed" is false — the else branch runs.
        @if = new { condition = new { type = "commandOutcome", stepRef = "gate", expectedState = "failed" } },
        body = Array.Empty<object>(),
        elseBody = new object[] { CommandStepPayload("tap-1", missCommandId, requireDispatch: true) }
      }
    };

    var execution = await CreateAndExecuteAsync(client, "if-else-require-dispatch-miss", steps).ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Failed");
  }

  [Fact]
  public async Task RequireDispatchStepNestedTwoLevelsDeepInsideLoopContainingIfFailsTheRun() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var okCommandId = await CreateDispatchingCommandAsync(client, "depth2-gate-cmd").ConfigureAwait(false);
    var missCommandId = await CreateNonDispatchingCommandAsync(client, "depth2-miss-cmd").ConfigureAwait(false);

    var steps = new object[] {
      new {
        stepId = "loop-1",
        stepType = "Loop",
        loop = new { loopType = "count", count = 1, maxIterations = 1 },
        body = new object[] {
          CommandStepPayload("gate", okCommandId, requireDispatch: false),
          new {
            stepId = "if-1",
            stepType = "If",
            @if = new { condition = new { type = "commandOutcome", stepRef = "gate", expectedState = "success" } },
            body = new object[] { CommandStepPayload("tap-1", missCommandId, requireDispatch: true) }
          }
        }
      }
    };

    var execution = await CreateAndExecuteAsync(client, "depth2-require-dispatch-miss", steps).ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Failed");
  }

  [Fact]
  public async Task RequireDispatchStepNestedInLoopBodySucceedsWhenItDoesDispatch() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var okCommandId = await CreateDispatchingCommandAsync(client, "loop-ok-cmd").ConfigureAwait(false);

    var steps = new object[] {
      new {
        stepId = "loop-1",
        stepType = "Loop",
        loop = new { loopType = "count", count = 1, maxIterations = 1 },
        body = new object[] { CommandStepPayload("tap-1", okCommandId, requireDispatch: true) }
      }
    };

    var execution = await CreateAndExecuteAsync(client, "loop-require-dispatch-ok", steps).ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Succeeded");
  }

  [Fact]
  public async Task TopLevelRequireDispatchStepStillFailsTheRunWhenItDoesNotDispatch() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var missCommandId = await CreateNonDispatchingCommandAsync(client, "top-level-miss-cmd").ConfigureAwait(false);

    var steps = new object[] { CommandStepPayload("tap-1", missCommandId, requireDispatch: true) };

    var execution = await CreateAndExecuteAsync(client, "top-level-require-dispatch-miss", steps).ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Failed");
  }

  [Fact]
  public async Task TopLevelRequireDispatchStepStillSucceedsWhenItDoesDispatch() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var okCommandId = await CreateDispatchingCommandAsync(client, "top-level-ok-cmd").ConfigureAwait(false);

    var steps = new object[] { CommandStepPayload("tap-1", okCommandId, requireDispatch: true) };

    var execution = await CreateAndExecuteAsync(client, "top-level-require-dispatch-ok", steps).ConfigureAwait(false);

    execution.GetProperty("status").GetString().Should().Be("Succeeded");
  }
}
