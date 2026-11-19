using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 193: a run through the ad-hoc route <c>POST /api/sequences/{id}/execute</c> with the body field
/// <c>parameters</c>. The steps are go-to-home-screen steps, which always dispatch in stub mode, so the
/// result depends only on the parameter condition.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class ParameterConditionRunTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public ParameterConditionRunTests() {
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

  private static object HomeStep(string stepId, object? condition = null) => new {
    stepId,
    stepType = "Action",
    condition,
    primitiveAction = new { type = "go-to-home-screen", schemaVersion = "v1", payload = new { } }
  };

  private static object Batch(string op, object value, bool negate = false) =>
    new { type = "parameter", name = "fighterBatch", @operator = op, value, negate };

  private static readonly object[] BatchParameter = { new { name = "fighterBatch", type = "number" } };

  private static readonly object[] TierParameter = {
    new {
      name = "fighterTier",
      type = "text",
      kind = "choice",
      options = new[] { new { label = "T1", value = "t1" }, new { label = "T7", value = "t7" }, new { label = "Max", value = "max" } }
    }
  };

  private static HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateAsync(HttpClient client, string name, object[] parameters, object[] steps) {
    var response = await client.PostAsJsonAsync("/api/sequences", new { name, version = 1, parameters, steps });
    var body = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateSessionAsync(HttpClient client) {
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "param-game", description = "desc" });
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = game!["id"]!.ToString() });
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    return session!["id"]!.ToString()!;
  }

  private static async Task<JsonElement> RunAsync(HttpClient client, string sequenceId, string parameter, string value) {
    var sessionId = await CreateSessionAsync(client);
    var response = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute",
      new { sessionId, parameters = new[] { new { name = parameter, value } } });
    var body = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return JsonDocument.Parse(body).RootElement.Clone();
  }

  private static List<JsonElement> Steps(JsonElement execution, string stepId) =>
    execution.GetProperty("steps").EnumerateArray()
      .Where(step => step.TryGetProperty("commandId", out var id) && id.GetString() == stepId)
      .ToList();

  private static bool Ran(JsonElement execution, string stepId) =>
    Steps(execution, stepId).Any(step => step.GetProperty("status").GetString() != "Skipped");

  [Theory]
  [InlineData("0", false)]
  [InlineData("250", true)]
  public async Task ANegatedEqualsZeroConditionSkipsTheStepOnlyForZero(string batch, bool runs) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var id = await CreateAsync(client, "param-batch-" + batch, BatchParameter,
      new[] { HomeStep("train", Batch("equals", 0, negate: true)) });

    var execution = await RunAsync(client, id, "fighterBatch", batch);

    execution.GetProperty("status").GetString().Should().Be("Succeeded");
    Ran(execution, "train").Should().Be(runs);
  }

  [Fact]
  public async Task ANumberParameterWithNoValueFailsTheRun() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var id = await CreateAsync(client, "param-no-value", BatchParameter, new[] { HomeStep("train", Batch("equals", 0)) });

    var sessionId = await CreateSessionAsync(client);
    var response = await client.PostAsJsonAsync($"/api/sequences/{id}/execute", new { sessionId });
    var execution = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    execution.GetProperty("status").GetString().Should().Be("Failed");
    execution.GetRawText().Should().Contain("parameter 'fighterBatch' used by field 'condition.name' could not be resolved");
    Steps(execution, "train").Should().ContainSingle().Which.GetProperty("status").GetString().Should().Be("Failed");
  }

  [Theory]
  [InlineData("MAX", false)]
  [InlineData("t7", true)]
  public async Task AnIfStepOnAChoiceParameterSelectsTheBranch(string tier, bool thenRuns) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var id = await CreateAsync(client, "param-tier-" + tier, TierParameter, new object[] {
      new {
        stepId = "check-tier",
        stepType = "If",
        @if = new { condition = new { type = "parameter", name = "fighterTier", @operator = "notEquals", value = "max" } },
        body = new[] { HomeStep("tap-tier") },
        elseBody = new[] { HomeStep("keep-tier") }
      }
    });

    var execution = await RunAsync(client, id, "fighterTier", tier);

    Ran(execution, "tap-tier").Should().Be(thenRuns);
    Ran(execution, "keep-tier").Should().Be(!thenRuns);
  }

  [Theory]
  [InlineData("5", 2)]
  [InlineData("0", 0)]
  public async Task AWhileLoopConditionUsesTheParameter(string batch, int bodyRuns) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var id = await CreateAsync(client, "param-while-" + batch, BatchParameter, new object[] {
      new {
        stepId = "loop",
        stepType = "Loop",
        loop = new { loopType = "while", condition = Batch("greaterThan", 0), maxIterations = 2, exitOnMaxIterations = true },
        body = new[] { HomeStep("body") }
      }
    });

    var execution = await RunAsync(client, id, "fighterBatch", batch);

    Steps(execution, "body").Count(step => step.GetProperty("status").GetString() != "Skipped").Should().Be(bodyRuns);
  }

  [Theory]
  [InlineData("0", 1)]
  [InlineData("250", 3)]
  public async Task ABreakConditionUsesTheParameter(string batch, int bodyRuns) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var id = await CreateAsync(client, "param-break-" + batch, BatchParameter, new object[] {
      new {
        stepId = "loop",
        stepType = "Loop",
        loop = new { loopType = "count", count = 3 },
        body = new object[] {
          HomeStep("body"),
          new { stepId = "stop", stepType = "Break", breakCondition = Batch("equals", 0) }
        }
      }
    });

    var execution = await RunAsync(client, id, "fighterBatch", batch);

    Steps(execution, "body").Count(step => step.GetProperty("status").GetString() != "Skipped").Should().Be(bodyRuns);
  }

  [Theory]
  [InlineData("5", true)]
  [InlineData("0", false)]
  public async Task AnAllCompositeOfAParameterAndACommandOutcome(string batch, bool thenRuns) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var id = await CreateAsync(client, "param-all-" + batch, BatchParameter, new object[] {
      HomeStep("probe"),
      new {
        stepId = "check",
        stepType = "If",
        @if = new {
          condition = new {
            type = "all",
            children = new object[] {
              Batch("greaterThan", 0),
              new { type = "commandOutcome", stepRef = "probe", expectedState = "success" }
            }
          }
        },
        body = new[] { HomeStep("train") }
      }
    });

    var execution = await RunAsync(client, id, "fighterBatch", batch);

    Ran(execution, "train").Should().Be(thenRuns);
  }
}
