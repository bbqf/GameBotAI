using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// The API contract of the commandStepOutcome condition (feature 167): create, read back, and update in each
/// slot where commandOutcome works.
/// </summary>
public sealed class CommandStepOutcomeConditionContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateCommandAsync(HttpClient client) {
    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = "cso-" + Guid.NewGuid().ToString("N"),
      steps = new object[] {
        new { type = "KeyInput", order = 1, keyInput = new { key = "Enter" } },
        new { type = "KeyInput", order = 2, keyInput = new { key = "Escape" } }
      }
    });
    var body = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
  }

  private static object CommandStep(string stepId, string commandId, object? condition = null) => condition is null
    ? new { stepId, stepType = "Action", primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } } }
    : new { stepId, stepType = "Action", primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } }, condition };

  private static object Leaf(string stepRef = "send-1", int order = 2, string state = "executed", bool negate = false) =>
    new { type = "commandStepOutcome", stepRef, commandStepOrder = order, expectedState = state, negate };

  private static async Task<JsonElement> CreateAsync(HttpClient client, object body) {
    var response = await client.PostAsJsonAsync("/api/sequences", body);
    var text = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.Created, text);
    return JsonDocument.Parse(text).RootElement.Clone();
  }

  private static async Task<JsonElement> ReadAsync(HttpClient client, string id) =>
    JsonDocument.Parse(await client.GetStringAsync(new Uri($"/api/sequences/{id}", UriKind.Relative))).RootElement.Clone();

  private static void AssertLeaf(JsonElement leaf, string state = "executed", bool negate = false) {
    leaf.GetProperty("type").GetString().Should().Be("commandStepOutcome");
    leaf.GetProperty("stepRef").GetString().Should().Be("send-1");
    leaf.GetProperty("commandStepOrder").GetInt32().Should().Be(2);
    leaf.GetProperty("expectedState").GetString().Should().Be(state);
    leaf.GetProperty("negate").GetBoolean().Should().Be(negate);
  }

  [Fact]
  public async Task AStepConditionSavesReadsBackAndUpdates() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);
    var name = "cso-step-" + Guid.NewGuid().ToString("N");

    var created = await CreateAsync(client, new {
      name, version = 1,
      steps = new[] { CommandStep("send-1", commandId), CommandStep("next", commandId, Leaf(negate: true)) }
    });
    var id = created.GetProperty("id").GetString()!;

    AssertLeaf((await ReadAsync(client, id)).GetProperty("steps")[1].GetProperty("condition"), negate: true);

    var update = await client.PutAsJsonAsync($"/api/sequences/{id}", new {
      name, version = 1,
      steps = new[] { CommandStep("send-1", commandId), CommandStep("next", commandId, Leaf(state: "image_detected")) }
    });
    update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
    AssertLeaf((await ReadAsync(client, id)).GetProperty("steps")[1].GetProperty("condition"), state: "image_detected");
  }

  [Fact]
  public async Task ACompositeChildSavesAndReadsBack() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);

    var created = await CreateAsync(client, new {
      name = "cso-all-" + Guid.NewGuid().ToString("N"), version = 1,
      steps = new[] {
        CommandStep("send-1", commandId),
        CommandStep("next", commandId, new { type = "all", children = new[] { Leaf(), Leaf(state: "skipped_detection_failed", negate: true) } })
      }
    });

    var children = (await ReadAsync(client, created.GetProperty("id").GetString()!))
      .GetProperty("steps")[1].GetProperty("condition").GetProperty("children");
    children.GetArrayLength().Should().Be(2);
    AssertLeaf(children[0]);
    AssertLeaf(children[1], "skipped_detection_failed", true);
  }

  [Fact]
  public async Task AnIfConditionAndABreakConditionSaveAndReadBack() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);

    var created = await CreateAsync(client, new {
      name = "cso-if-" + Guid.NewGuid().ToString("N"), version = 1,
      steps = new object[] {
        CommandStep("send-1", commandId),
        new {
          stepId = "if-1", stepType = "If", @if = new { condition = Leaf() },
          body = new[] { CommandStep("then-1", commandId) }
        },
        new {
          stepId = "loop-1", stepType = "Loop", loop = new { loopType = "count", count = 3 },
          body = new object[] {
            CommandStep("again", commandId),
            new { stepId = "end-run", stepType = "Break", breakCondition = Leaf() }
          }
        }
      }
    });

    var read = await ReadAsync(client, created.GetProperty("id").GetString()!);
    AssertLeaf(read.GetProperty("steps")[1].GetProperty("if").GetProperty("condition"));
    AssertLeaf(read.GetProperty("steps")[2].GetProperty("body")[1].GetProperty("breakCondition"));
  }

  [Fact]
  public async Task AWhileLoopConditionSavesAndReadsBack() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);

    var created = await CreateAsync(client, new {
      name = "cso-while-" + Guid.NewGuid().ToString("N"), version = 1,
      steps = new object[] {
        CommandStep("send-1", commandId),
        new {
          stepId = "loop-1", stepType = "Loop",
          loop = new { loopType = "while", condition = new { type = "all", children = new[] { Leaf() } }, maxIterations = 3 },
          body = new[] { CommandStep("again", commandId) }
        }
      }
    });

    var read = await ReadAsync(client, created.GetProperty("id").GetString()!);
    read.GetProperty("steps")[1].GetProperty("loop").GetRawText().Should().Contain("commandStepOutcome");
  }
}
