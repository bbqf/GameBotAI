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
/// Feature 167: a bad commandStepOutcome condition gives HTTP 400, never 500. The order check against the
/// command runs at save and at the save dry run only.
/// </summary>
public sealed class CommandStepOutcomeValidationContractTests {
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
      name = "csov-" + Guid.NewGuid().ToString("N"),
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

  private static object Body(string commandId, object condition, bool dryRun = false, object? extraStep = null) {
    var steps = new System.Collections.Generic.List<object> { CommandStep("send-1", commandId) };
    if (extraStep is not null) steps.Add(extraStep);
    steps.Add(CommandStep("next", commandId, condition));
    return dryRun
      ? new { name = "csov-" + Guid.NewGuid().ToString("N"), version = 1, dryRun = true, steps }
      : new { name = "csov-" + Guid.NewGuid().ToString("N"), version = 1, steps };
  }

  private static async Task<string> ErrorsAsync(HttpResponseMessage response) {
    var content = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
    return string.Join(" | ", JsonDocument.Parse(content).RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
  }

  [Theory]
  [InlineData("empty-stepref", "Step 'next' commandStepOutcome condition requires stepRef.")]
  [InlineData("unknown-step", "Step 'next' commandStepOutcome references unknown prior step 'nope'.")]
  [InlineData("order-zero", "Step 'next' commandStepOutcome commandStepOrder must be 1 or more.")]
  [InlineData("bad-state", "Step 'next' commandStepOutcome expectedState must be one of executed|skipped_detection_failed|completed_timeout|image_detected.")]
  [InlineData("reason-code", "Step 'next' commandStepOutcome expectedState must be one of executed|skipped_detection_failed|completed_timeout|image_detected.")]
  public async Task EachBadFieldGivesA400WithTheMessage(string kind, string expected) {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);
    object condition = kind switch {
      "empty-stepref" => new { type = "commandStepOutcome", stepRef = "", commandStepOrder = 2, expectedState = "executed" },
      "unknown-step" => new { type = "commandStepOutcome", stepRef = "nope", commandStepOrder = 2, expectedState = "executed" },
      "order-zero" => new { type = "commandStepOutcome", stepRef = "send-1", commandStepOrder = 0, expectedState = "executed" },
      "bad-state" => new { type = "commandStepOutcome", stepRef = "send-1", commandStepOrder = 2, expectedState = "success" },
      _ => new { type = "commandStepOutcome", stepRef = "send-1", commandStepOrder = 2, expectedState = "detection_failed_after_3_retries" }
    };

    var create = await client.PostAsJsonAsync("/api/sequences", Body(commandId, condition));
    var dryRun = await client.PostAsJsonAsync("/api/sequences", Body(commandId, condition, dryRun: true));

    (await ErrorsAsync(create)).Should().Contain(expected);
    (await ErrorsAsync(dryRun)).Should().Contain(expected);
  }

  [Fact]
  public async Task AMissingFieldGivesA400AndNot500() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);

    var response = await client.PostAsJsonAsync("/api/sequences", Body(commandId, new { type = "commandStepOutcome" }));

    var errors = await ErrorsAsync(response);
    errors.Should().Contain("requires stepRef").And.Contain("commandStepOrder").And.Contain("expectedState");
  }

  [Fact]
  public async Task AStepThatDoesNotRunACommandGivesA400() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);
    var tap = new { stepId = "tap-1", stepType = "Action", primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } } };
    var condition = new { type = "commandStepOutcome", stepRef = "tap-1", commandStepOrder = 2, expectedState = "executed" };

    var response = await client.PostAsJsonAsync("/api/sequences", Body(commandId, condition, extraStep: tap));

    (await ErrorsAsync(response)).Should().Contain("stepRef 'tap-1' must reference a step that runs a command");
  }

  [Fact]
  public async Task AStepThatIsNotBeforeGivesA400() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);
    var condition = new { type = "commandStepOutcome", stepRef = "later", commandStepOrder = 2, expectedState = "executed" };
    var body = new {
      name = "csov-" + Guid.NewGuid().ToString("N"), version = 1,
      steps = new[] { CommandStep("next", commandId, condition), CommandStep("later", commandId) }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", body);

    (await ErrorsAsync(response)).Should().Contain("stepRef 'later' must reference a prior step");
  }

  [Fact]
  public async Task AnOrderThatTheCommandDoesNotHaveGivesA400AtSaveAndAtSaveDryRun() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);
    var condition = new { type = "commandStepOutcome", stepRef = "send-1", commandStepOrder = 7, expectedState = "executed" };
    var expected = $"Step 'next' commandStepOutcome: command '{commandId}' has no step with order 7.";

    var create = await client.PostAsJsonAsync("/api/sequences", Body(commandId, condition));
    var dryRun = await client.PostAsJsonAsync("/api/sequences", Body(commandId, condition, dryRun: true));

    (await ErrorsAsync(create)).Should().Contain(expected);
    (await ErrorsAsync(dryRun)).Should().Contain(expected);
  }

  [Fact]
  public async Task AnOrderThatTheCommandHasIsAccepted() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);
    var condition = new { type = "commandStepOutcome", stepRef = "send-1", commandStepOrder = 1, expectedState = "executed" };

    var response = await client.PostAsJsonAsync("/api/sequences", Body(commandId, condition));

    response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task TheRunDryRunDoesNotCheckTheOrderAgainstTheCommand() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);
    // Save with a good order, then run a dry run: no order check at run time, and the run does not fail.
    var good = new { type = "commandStepOutcome", stepRef = "send-1", commandStepOrder = 1, expectedState = "executed" };
    var create = await client.PostAsJsonAsync("/api/sequences", Body(commandId, good));
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

    var run = await client.PostAsJsonAsync($"/api/sequences/{id}/execute", new { dryRun = true });

    run.StatusCode.Should().Be(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
    var result = JsonDocument.Parse(await run.Content.ReadAsStringAsync()).RootElement;
    result.GetProperty("status").GetString().Should().NotBe("Failed");
  }
}
