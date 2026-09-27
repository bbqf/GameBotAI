using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

/// <summary>
/// Issue #221 (B-020): when two or more steps use the same command, each command node in
/// <c>GET /api/execution-logs/{id}/subtree</c> must have the stepId of the step that ran it, in
/// <c>message</c> and in <c>deepLink.stepId</c>. Before the fix, all nodes had the stepId of the
/// first step that uses the command, also when that step did not run.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SharedCommandStepAttributionIntegrationTests : IDisposable {
  private const string SharedCommandName = "shared-cmd";
  private static readonly string[] ControlFlowStepIds = { "if-false", "if-true", "if-else", "loop-1" };

  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public SharedCommandStepAttributionIntegrationTests() {
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

  // A GoToHomeScreen step is a plain key input. In stub mode (GAMEBOT_USE_ADB=false) the device
  // accepts it, so the command step has the outcome "executed".
  private static async Task<string> CreateSharedCommandAsync(HttpClient client) {
    var commandReq = new {
      name = SharedCommandName,
      triggerId = (string?)null,
      steps = new[] { new { type = "GoToHomeScreen", order = 0 } }
    };
    var resp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    var created = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    return created!["id"]!.ToString()!;
  }

  // A "command" step needs a real session.
  private static async Task<string> CreateSessionAsync(HttpClient client) {
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "shared-cmd-game", description = "desc" }).ConfigureAwait(false);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    var gameId = game!["id"]!.ToString();

    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(false);
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    return session!["id"]!.ToString()!;
  }

  private static object CommandStep(string stepId, string commandId) => new {
    stepId,
    stepType = "Action",
    primitiveAction = new {
      type = "command",
      schemaVersion = "v1",
      payload = new { commandId }
    }
  };

  private static object GateCondition(string expectedState)
    => new { condition = new { type = "commandOutcome", stepRef = "gate", expectedState } };

  private static HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  /// <summary>Creates and runs the sequence, then returns the root node of its execution-log subtree.</summary>
  private static async Task<JsonElement> RunAndReadSubtreeAsync(HttpClient client, string name, object[] steps) {
    var sessionId = await CreateSessionAsync(client).ConfigureAwait(false);

    var createResponse = await client.PostAsJsonAsync("/api/sequences", new { name, version = 1, steps }).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { sessionId }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    execution.GetProperty("status").GetString().Should().Be("Succeeded");

    var list = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution-logs?objectType=sequence&objectId={sequenceId}&pageSize=10", UriKind.Relative)).ConfigureAwait(false);
    var executionId = list.GetProperty("items").EnumerateArray()
      .Where(item => string.Equals(item.GetProperty("executionType").GetString(), "sequence", StringComparison.OrdinalIgnoreCase))
      .Select(item => item.GetProperty("id").GetString())
      .Single();

    var subtree = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/execution-logs/{executionId}/subtree", UriKind.Relative)).ConfigureAwait(false);
    return subtree.GetProperty("root");
  }

  private static IEnumerable<JsonElement> AllNodes(JsonElement node) {
    yield return node;
    if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array) {
      foreach (var child in children.EnumerateArray()) {
        foreach (var descendant in AllNodes(child)) {
          yield return descendant;
        }
      }
    }
  }

  private static string? MessageOf(JsonElement node)
    => node.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null;

  private static string? DeepLinkStepIdOf(JsonElement node)
    => node.TryGetProperty("deepLink", out var deepLink)
       && deepLink.ValueKind == JsonValueKind.Object
       && deepLink.TryGetProperty("stepId", out var stepId)
       && stepId.ValueKind == JsonValueKind.String
      ? stepId.GetString()
      : null;

  private static List<JsonElement> CommandNodes(JsonElement root)
    => AllNodes(root)
      .Where(node => MessageOf(node)?.Contains($"ran command '{SharedCommandName}'", StringComparison.Ordinal) == true)
      .ToList();

  private static void EachCommandNodeNamesItsOwnStep(IEnumerable<JsonElement> commandNodes) {
    foreach (var node in commandNodes) {
      var stepId = DeepLinkStepIdOf(node);
      stepId.Should().NotBeNullOrWhiteSpace();
      MessageOf(node).Should().StartWith($"Step '{stepId}' ran command '{SharedCommandName}' with outcome 'executed'");
    }
  }

  [Fact]
  public async Task TopLevelStepsThatShareACommandEachHaveTheirOwnStepIdInTheLog() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var commandId = await CreateSharedCommandAsync(client).ConfigureAwait(false);

    var steps = new object[] {
      CommandStep("first", commandId),
      CommandStep("second", commandId),
      CommandStep("third", commandId)
    };

    var root = await RunAndReadSubtreeAsync(client, "shared-top-level", steps).ConfigureAwait(false);

    var commandNodes = CommandNodes(root);
    commandNodes.Select(DeepLinkStepIdOf).Should().Equal("first", "second", "third");
    EachCommandNodeNamesItsOwnStep(commandNodes);
  }

  [Fact]
  public async Task NestedStepsThatShareACommandEachHaveTheirOwnStepIdInTheLog() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var commandId = await CreateSharedCommandAsync(client).ConfigureAwait(false);

    var steps = new object[] {
      CommandStep("gate", commandId),
      new {
        stepId = "if-false",
        stepType = "If",
        @if = GateCondition("failed"),
        body = new object[] { CommandStep("body-false", commandId) }
      },
      new {
        stepId = "if-true",
        stepType = "If",
        @if = GateCondition("success"),
        body = new object[] { CommandStep("body-true", commandId) }
      },
      new {
        stepId = "if-else",
        stepType = "If",
        @if = GateCondition("failed"),
        body = Array.Empty<object>(),
        elseBody = new object[] { CommandStep("else-step", commandId) }
      },
      new {
        stepId = "loop-1",
        stepType = "Loop",
        loop = new { loopType = "count", count = 2, maxIterations = 2 },
        body = new object[] { CommandStep("loop-step", commandId) }
      }
    };

    var root = await RunAndReadSubtreeAsync(client, "shared-nested", steps).ConfigureAwait(false);

    var commandNodes = CommandNodes(root);
    commandNodes.Select(DeepLinkStepIdOf).Should().Equal("gate", "body-true", "else-step", "loop-step", "loop-step");
    EachCommandNodeNamesItsOwnStep(commandNodes);

    // The body of an If whose condition was false did not run, so no node has its id.
    AllNodes(root).Select(DeepLinkStepIdOf).Should().NotContain("body-false");

    // If and Loop nodes keep their own ids.
    AllNodes(root).Select(DeepLinkStepIdOf).Should().Contain(ControlFlowStepIds);
  }
}
