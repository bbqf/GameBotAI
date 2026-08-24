using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 078: the additive parameter members on the command and sequence APIs — declarations,
/// step bindings, the numeric field-template overlay, the read-only parameter-scope endpoints, and
/// every blocking error code from contracts/api.md.
/// </summary>
public sealed class ParameterContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static System.Net.Http.HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  /// <summary>A key-input command whose key comes from <paramref name="key"/>.</summary>
  private static object KeyCommandPayload(string name, string key, params object[] parameters) => new {
    name,
    parameters,
    steps = new object[] {
      new { type = "KeyInput", order = 0, keyInput = new { key } }
    }
  };

  private static string? ErrorCode(JsonElement root) =>
      root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
          ? error.GetString()
          : null;

  // ── Declarations round-trip ──────────────────────────────────────────────

  [Fact]
  public async Task CommandDeclarationsRoundTripWithEveryField() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var create = await client.PostAsJsonAsync("/api/commands", KeyCommandPayload(
        "param-roundtrip",
        "{{keyName}}",
        new {
          name = "keyName",
          type = "text",
          @default = "KEYCODE_HOME",
          required = true,
          description = "Which key to press."
        })).ConfigureAwait(false);

    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    var id = created.GetProperty("id").GetString();

    var get = await client.GetAsync(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    var fetched = JsonDocument.Parse(await get.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;

    var declaration = fetched.GetProperty("parameters").EnumerateArray().Single();
    declaration.GetProperty("name").GetString().Should().Be("keyName");
    declaration.GetProperty("type").GetString().Should().Be("text");
    declaration.GetProperty("default").GetString().Should().Be("KEYCODE_HOME");
    declaration.GetProperty("required").GetBoolean().Should().BeTrue();
    declaration.GetProperty("description").GetString().Should().Be("Which key to press.");
  }

  [Fact]
  public async Task AnUnparametrizedCommandOmitsTheParametersMember() {
    // FR-032: pre-feature payloads must not gain a member, so nothing in the store is rewritten.
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var create = await client.PostAsJsonAsync("/api/commands", new {
      name = "no-params",
      steps = new object[] { new { type = "KeyInput", order = 0, keyInput = new { key = "KEYCODE_HOME" } } }
    }).ConfigureAwait(false);

    var body = await create.Content.ReadAsStringAsync().ConfigureAwait(false);

    create.StatusCode.Should().Be(HttpStatusCode.Created);
    body.Should().NotContain("\"parameters\"");
  }

  [Fact]
  public async Task NumericFieldTemplateOverlayRoundTrips() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var create = await client.PostAsJsonAsync("/api/commands", new {
      name = "overlay-roundtrip",
      parameters = new object[] { new { name = "originX", type = "number" } },
      steps = new object[] {
        new {
          type = "Swipe",
          order = 0,
          swipe = new { startX = 0, startY = 10, endX = 20, endY = 30 },
          fieldTemplates = new Dictionary<string, string> { ["swipe.startX"] = "{{originX}}" }
        }
      }
    }).ConfigureAwait(false);

    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    var step = created.GetProperty("steps").EnumerateArray().Single();
    step.GetProperty("fieldTemplates").GetProperty("swipe.startX").GetString().Should().Be("{{originX}}");
  }

  // ── Blocking error codes ─────────────────────────────────────────────────

  [Fact]
  public async Task ReservedIterationNameIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", KeyCommandPayload(
        "reserved-name", "KEYCODE_HOME", new { name = "iteration" })).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    ErrorCode(root).Should().Be("invalid_parameter_declaration");
  }

  [Fact]
  public async Task ReservedQueueNamespaceIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", KeyCommandPayload(
        "reserved-ns", "KEYCODE_HOME", new { name = "queue.emulatorSerial" })).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    ErrorCode(JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement)
        .Should().Be("invalid_parameter_declaration");
  }

  [Fact]
  public async Task NumericDefaultThatIsNotAWholeNumberIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", KeyCommandPayload(
        "bad-default", "KEYCODE_HOME",
        new { name = "waitMs", type = "number", @default = "soon" })).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    ErrorCode(JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement)
        .Should().Be("invalid_parameter_default");
  }

  [Fact]
  public async Task ReferenceToAnUndeclaredNameIsRejectedWithTheOffendingField() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands",
        KeyCommandPayload("unresolvable", "{{typo}}")).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    ErrorCode(root).Should().Be("unresolvable_parameter_reference");
    // FR-029: the detail names the field and the parameter so the UI can anchor the message.
    var detail = root.GetProperty("details").EnumerateArray().First();
    detail.GetProperty("fieldPath").GetString().Should().Be("keyInput.key");
    detail.GetProperty("parameterName").GetString().Should().Be("typo");
  }

  [Fact]
  public async Task AQueueBuiltInNeedsNoDeclarationAndIsAccepted() {
    // The motivating case: referencing the queue's own serial requires nothing to be declared.
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands",
        KeyCommandPayload("built-in-ok", "{{queue.emulatorSerial}}")).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  [Fact]
  public async Task UnknownFieldTemplatePathIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = "bad-overlay",
      parameters = new object[] { new { name = "x", type = "number" } },
      steps = new object[] {
        new {
          type = "KeyInput",
          order = 0,
          keyInput = new { key = "KEYCODE_HOME" },
          fieldTemplates = new Dictionary<string, string> { ["nonsense.path"] = "{{x}}" }
        }
      }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    ErrorCode(JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement)
        .Should().Be("unknown_field_template_path");
  }

  [Fact]
  public async Task PlaceholderInACommandReferenceFieldIsRejected() {
    // FR-007: references stay literal so the dangling-reference check keeps working.
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = "ref-placeholder",
      parameters = new object[] { new { name = "which" } },
      steps = new object[] { new { type = "Command", order = 0, targetId = "{{which}}" } }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    ErrorCode(JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement)
        .Should().Be("parameter_in_reference_field");
  }

  // ── Parameter-scope endpoints ────────────────────────────────────────────

  [Fact]
  public async Task CommandParameterScopeReturnsDeclarationsAndQueueBuiltIns() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var create = await client.PostAsJsonAsync("/api/commands", KeyCommandPayload(
        "scope-command", "{{keyName}}",
        new { name = "keyName", description = "Which key." })).ConfigureAwait(false);
    var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("id").GetString();

    var scope = await client.GetAsync(new Uri($"/api/commands/{id}/parameter-scope", UriKind.Relative))
        .ConfigureAwait(false);

    scope.StatusCode.Should().Be(HttpStatusCode.OK);
    var entries = JsonDocument.Parse(await scope.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("entries").EnumerateArray().ToList();

    entries.Should().Contain(e => e.GetProperty("name").GetString() == "keyName"
        && e.GetProperty("declared").GetBoolean());
    foreach (var builtIn in new[] { "queue.emulatorSerial", "queue.instanceName", "queue.instanceIndex", "queue.gameId" }) {
      entries.Should().Contain(e => e.GetProperty("name").GetString() == builtIn
          && e.GetProperty("originLayer").GetString() == "queue");
    }
  }

  [Fact]
  public async Task CommandParameterScopeIsNotFoundForAnUnknownCommand() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var scope = await client.GetAsync(new Uri("/api/commands/does-not-exist/parameter-scope", UriKind.Relative))
        .ConfigureAwait(false);

    scope.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task SequenceParameterScopeReportsStepCalleeDeclarations() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var createCommand = await client.PostAsJsonAsync("/api/commands", KeyCommandPayload(
        "callee", "{{keyName}}", new { name = "keyName" })).ConfigureAwait(false);
    var commandId = JsonDocument.Parse(await createCommand.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("id").GetString();

    var createSequence = await client.PostAsJsonAsync("/api/sequences", new {
      name = "scope-sequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "s1",
          stepType = "Action",
          primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } }
        }
      }
    }).ConfigureAwait(false);
    createSequence.StatusCode.Should().Be(HttpStatusCode.Created);
    var sequenceId = JsonDocument.Parse(await createSequence.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("id").GetString();

    var scope = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}/parameter-scope", UriKind.Relative))
        .ConfigureAwait(false);

    scope.StatusCode.Should().Be(HttpStatusCode.OK);
    var root = JsonDocument.Parse(await scope.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    var callee = root.GetProperty("stepCallees").EnumerateArray().Single();
    callee.GetProperty("commandId").GetString().Should().Be(commandId);
    callee.GetProperty("parameters").EnumerateArray()
        .Should().Contain(p => p.GetProperty("name").GetString() == "keyName");
  }

  // ── Ad-hoc execute ───────────────────────────────────────────────────────

  [Fact]
  public async Task ExecutingASequenceWithAnUnsuppliedRequiredParameterIsRefused() {
    // FR-031: an ad-hoc run has no queue, so nothing can supply a required parameter implicitly.
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var create = await client.PostAsJsonAsync("/api/sequences", new {
      name = "requires-param",
      version = 1,
      parameters = new object[] { new { name = "mustSupply", required = true } },
      steps = new object[] {
        new { stepId = "s1", stepType = "Action", primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 2 } } }
      }
    }).ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("id").GetString();

    var execute = await client.PostAsJsonAsync($"/api/sequences/{id}/execute", new { }).ConfigureAwait(false);

    execute.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var root = JsonDocument.Parse(await execute.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    ErrorCode(root).Should().Be("missing_required_parameters");
    root.GetProperty("parameters").EnumerateArray()
        .Select(p => p.GetString()).Should().Contain("mustSupply");
  }
}
