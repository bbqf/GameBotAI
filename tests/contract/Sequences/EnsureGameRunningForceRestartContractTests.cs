using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1307, CA1308

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 129: the API contract of the forceRestart option of the ensure-game-running action and of the
/// command step. Valid values are accepted and read back. A value that is not true or false gives 400 and
/// never 500, for a top-level step, a loop-body step, and an if-branch step.
/// </summary>
public sealed class EnsureGameRunningForceRestartContractTests {
  private const string OneByOnePngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var response = await client.PostAsJsonAsync(
      new Uri("/api/images", UriKind.Relative),
      new { id = "fr-img", data = OneByOnePngBase64 }).ConfigureAwait(false);
    response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict);
    return client;
  }

  private static object EnsureStep(string stepId, JsonElement? forceRestart, object? parameterBindings = null) {
    var payload = new Dictionary<string, object?>();
    if (forceRestart is { } value) payload["forceRestart"] = value;
    return parameterBindings is null
      ? new {
        stepId,
        stepType = "Action",
        primitiveAction = new { type = "ensure-game-running", schemaVersion = "v1", payload }
      }
      : new {
        stepId,
        stepType = "Action",
        primitiveAction = new { type = "ensure-game-running", schemaVersion = "v1", payload },
        parameterBindings
      };
  }

  private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

  public enum Place { Top, LoopBody, IfBranch }

  private static object Sequence(string name, Place place, object ensureStep) => place switch {
    Place.Top => new { name, version = 1, steps = new[] { ensureStep } },
    Place.LoopBody => new {
      name,
      version = 1,
      steps = new object[] {
        new { stepId = "loop1", stepType = "Loop", loop = new { loopType = "count", count = 2 }, body = new[] { ensureStep } }
      }
    },
    _ => new {
      name,
      version = 1,
      steps = new object[] {
        new {
          stepId = "if1",
          stepType = "If",
          @if = new { condition = new { type = "imageVisible", imageId = "fr-img", minSimilarity = 0.85 } },
          body = new[] { ensureStep }
        }
      }
    }
  };

  private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

  [Theory]
  [InlineData("true")]
  [InlineData("false")]
  public async Task TrueAndFalseAreAcceptedAndReadBack(string literal) {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", Sequence(UniqueName("fr-ok"), Place.Top, EnsureStep("restart", Json(literal)))).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);

    var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString();
    var read = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    var readBody = await read.Content.ReadAsStringAsync().ConfigureAwait(false);
    read.StatusCode.Should().Be(HttpStatusCode.OK, readBody);
    var payload = JsonDocument.Parse(readBody).RootElement.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload");
    payload.GetProperty("forceRestart").GetBoolean().Should().Be(bool.Parse(literal));
  }

  [Fact]
  public async Task AStepWithoutTheOptionIsAccepted() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", Sequence(UniqueName("fr-plain"), Place.Top, EnsureStep("plain", null))).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  public static TheoryData<string, Place> BadCases() {
    var data = new TheoryData<string, Place>();
    foreach (var literal in new[] { "\"true\"", "1", "null", "{}", "[]", "\"{{x}}\"" }) {
      foreach (var place in new[] { Place.Top, Place.LoopBody, Place.IfBranch }) {
        data.Add(literal, place);
      }
    }
    return data;
  }

  [Theory]
  [MemberData(nameof(BadCases))]
  public async Task ABadValueGives400NeverFiveHundred(string literal, Place place) {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", Sequence(UniqueName("fr-bad"), place, EnsureStep("bad", Json(literal)))).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "value {0} in {1} returned {2}: {3}", literal, place, response.StatusCode, body);
    body.Should().Contain("forceRestart");
  }

  [Fact]
  public async Task AnUpdateWithABadValueGives400() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var created = await client.PostAsJsonAsync("/api/sequences", Sequence(UniqueName("fr-upd"), Place.Top, EnsureStep("s", Json("true")))).ConfigureAwait(false);
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement.GetProperty("id").GetString();

    var response = await client.PutAsJsonAsync($"/api/sequences/{id}", Sequence(UniqueName("fr-upd2"), Place.Top, EnsureStep("s", Json("\"yes\"")))).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task AStepParameterBindingThatTargetsForceRestartGives400() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var step = EnsureStep("bound", null, parameterBindings: new[] { new { name = "forceRestart", value = "true" } });

    var response = await client.PostAsJsonAsync("/api/sequences", Sequence(UniqueName("fr-bind"), Place.Top, step)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("forceRestart");
  }

  [Fact]
  public async Task ExpectedStateRestartedIsAcceptedAndAnUnknownStateIsRejected() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    object Gate(string state) => new {
      name = UniqueName("fr-state"),
      version = 1,
      steps = new object[] {
        EnsureStep("restart", Json("true")),
        new {
          stepId = "gate",
          stepType = "Action",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } },
          condition = new { type = "commandOutcome", stepRef = "restart", expectedState = state }
        }
      }
    };

    var ok = await client.PostAsJsonAsync("/api/sequences", Gate("restarted")).ConfigureAwait(false);
    ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync().ConfigureAwait(false));

    var bad = await client.PostAsJsonAsync("/api/sequences", Gate("rebooted")).ConfigureAwait(false);
    bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await bad.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain("restarted");
  }

  // ── commands ───────────────────────────────────────────────────────────────

  private static object CommandBody(string name, JsonElement? forceRestart) {
    var config = new Dictionary<string, object?>();
    if (forceRestart is { } value) config["forceRestart"] = value;
    return new {
      name,
      steps = new object[] { new { type = "EnsureGameRunning", order = 1, ensureGameRunning = config } }
    };
  }

  [Fact]
  public async Task ACommandStepWithForceRestartIsCreatedUpdatedAndReadBack() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var create = await client.PostAsJsonAsync("/api/commands", CommandBody(UniqueName("cmd-fr"), Json("true"))).ConfigureAwait(false);
    var createBody = await create.Content.ReadAsStringAsync().ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created, createBody);
    var root = JsonDocument.Parse(createBody).RootElement;
    root.GetProperty("steps")[0].GetProperty("ensureGameRunning").GetProperty("forceRestart").GetBoolean().Should().BeTrue();

    var id = root.GetProperty("id").GetString();
    var update = await client.PatchAsJsonAsync($"/api/commands/{id}", CommandBody(UniqueName("cmd-fr2"), Json("false"))).ConfigureAwait(false);
    var updateBody = await update.Content.ReadAsStringAsync().ConfigureAwait(false);
    update.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    if (update.StatusCode == HttpStatusCode.OK) {
      var steps = JsonDocument.Parse(updateBody).RootElement.GetProperty("steps")[0];
      var hasConfig = steps.TryGetProperty("ensureGameRunning", out var cfg) && cfg.ValueKind == JsonValueKind.Object;
      if (hasConfig && cfg.TryGetProperty("forceRestart", out var flag) && flag.ValueKind != JsonValueKind.Null) {
        flag.GetBoolean().Should().BeFalse();
      }
    }
  }

  [Fact]
  public async Task ACommandStepWithAStringValueGives400() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/commands", CommandBody(UniqueName("cmd-bad"), Json("\"yes\""))).ConfigureAwait(false);

    ((int)response.StatusCode).Should().BeInRange(400, 499, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  // ── OpenAPI ────────────────────────────────────────────────────────────────

  [Fact]
  public async Task TheOpenApiDocumentListsForceRestart() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var text = await client.GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);

    text.Should().Contain("forceRestart");
    text.Should().Contain("restarted");
  }
}
