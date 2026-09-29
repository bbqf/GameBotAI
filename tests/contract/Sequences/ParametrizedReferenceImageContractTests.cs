using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Images;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 114: a parameter selects the reference image. The command part covers the two image keys
/// of <c>fieldTemplates</c>. The sequence part covers a placeholder in <c>imageVisible.imageId</c>.
/// </summary>
public sealed class ParametrizedReferenceImageContractTests {
  private const string TapImageKey = "primitiveTap.detectionTarget.referenceImageId";
  private const string WaitImageKey = "waitForImage.detectionTarget.referenceImageId";

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

  private static async Task<JsonElement> ReadJsonAsync(System.Net.Http.HttpResponseMessage response) =>
      JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;

  private static object[] NovaDeclaration() =>
      new object[] { new { name = "novaOption", type = "text", required = true } };

  private static object TapStep(string key, string value) => new {
    type = "PrimitiveTap",
    order = 0,
    primitiveTap = new { detectionTarget = new { referenceImageId = "option-a", confidence = 0.85 } },
    fieldTemplates = new Dictionary<string, string> { [key] = value }
  };

  private static object CommandPayload(string name, string tapValue = "{{novaOption}}", bool declare = true) => new {
    name,
    parameters = declare ? NovaDeclaration() : Array.Empty<object>(),
    steps = new object[] {
      TapStep(TapImageKey, tapValue),
      new {
        type = "WaitForImage",
        order = 1,
        waitForImage = new { detectionTarget = new { referenceImageId = "option-a" }, timeoutMs = 3000 },
        fieldTemplates = new Dictionary<string, string> { [WaitImageKey] = "{{novaOption}}" }
      }
    }
  };

  // ── Section 1: command image keys ────────────────────────────────────────

  [Fact]
  public async Task CommandWithBothImageKeysIsCreatedWithWarnings() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", CommandPayload("tap-selected-option"))
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var root = await ReadJsonAsync(response).ConfigureAwait(false);
    var steps = root.GetProperty("steps").EnumerateArray().ToList();
    steps[0].GetProperty("fieldTemplates").GetProperty(TapImageKey).GetString().Should().Be("{{novaOption}}");
    steps[1].GetProperty("fieldTemplates").GetProperty(WaitImageKey).GetString().Should().Be("{{novaOption}}");

    var warnings = root.GetProperty("warnings").EnumerateArray()
        .Where(w => w.GetProperty("code").GetString() == "static_check_skipped")
        .Select(w => w.GetProperty("fieldPath").GetString())
        .ToList();
    warnings.Count(p => p == TapImageKey).Should().Be(1);
    warnings.Count(p => p == WaitImageKey).Should().Be(1);
  }

  [Theory]
  [InlineData("nova-{{option}}")]
  [InlineData("option-a")]
  public async Task ImageKeyValueThatIsNotOneWholePlaceholderIsRejected(string value) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = "bad-image-value",
      parameters = new object[] { new { name = "novaOption" }, new { name = "option" } },
      steps = new object[] { TapStep(TapImageKey, value) }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var root = await ReadJsonAsync(response).ConfigureAwait(false);
    root.GetProperty("error").GetString().Should().Be("invalid_field_template_value");
    root.GetProperty("message").GetString().Should().Be(
        $"Step 0: the value of '{TapImageKey}' must be one whole placeholder, for example {{{{name}}}}.");
    root.GetProperty("details").EnumerateArray().First().GetProperty("fieldPath").GetString()
        .Should().Be(TapImageKey);
  }

  [Fact]
  public async Task UnsupportedKeyGivesTheNewMessage() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = "bad-image-key",
      parameters = new object[] { new { name = "x" } },
      steps = new object[] { TapStep("ensureGameRunning.readinessImage.referenceImageId", "{{x}}") }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var root = await ReadJsonAsync(response).ConfigureAwait(false);
    root.GetProperty("error").GetString().Should().Be("unknown_field_template_path");
    root.GetProperty("message").GetString().Should().Be(
        "Step 0: 'ensureGameRunning.readinessImage.referenceImageId' is not a parametrizable field.");
  }

  [Fact]
  public async Task UndeclaredNameInAnImageKeyIsUnresolvable() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands",
        CommandPayload("undeclared-image", declare: false)).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var root = await ReadJsonAsync(response).ConfigureAwait(false);
    root.GetProperty("error").GetString().Should().Be("unresolvable_parameter_reference");
  }

  [Fact]
  public async Task PatchAppliesTheSameRules() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var create = await client.PostAsJsonAsync("/api/commands", CommandPayload("patch-image"))
        .ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = (await ReadJsonAsync(create).ConfigureAwait(false)).GetProperty("id").GetString();

    var bad = await client.PatchAsJsonAsync($"/api/commands/{id}", new {
      steps = new object[] { TapStep(TapImageKey, "nova-{{novaOption}}") }
    }).ConfigureAwait(false);
    bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ReadJsonAsync(bad).ConfigureAwait(false)).GetProperty("error").GetString()
        .Should().Be("invalid_field_template_value");

    var good = await client.PatchAsJsonAsync($"/api/commands/{id}", new {
      steps = new object[] { TapStep(TapImageKey, "{{novaOption}}") }
    }).ConfigureAwait(false);
    good.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ReadJsonAsync(good).ConfigureAwait(false)).GetProperty("warnings").EnumerateArray()
        .Should().Contain(w => w.GetProperty("code").GetString() == "static_check_skipped"
            && w.GetProperty("fieldPath").GetString() == TapImageKey);
  }

  [Fact]
  public async Task NumericKeyWithALiteralValueIsStillAccepted() {
    // FR-013: the numeric keys keep their behavior; the save does not check their value.
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = "numeric-literal",
      steps = new object[] { TapStep("primitiveTap.detectionTarget.offsetX", "12") }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  // ── Section 2: sequence imageVisible.imageId placeholder ─────────────────

  private const string OneByOnePngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  /// <summary>
  /// Stores a reference image through the repository. The upload route is not available on each
  /// host, and the sequence save only asks the repository.
  /// </summary>
  private static async Task RegisterImageAsync(WebApplicationFactory<Program> app, string imageId) {
    var images = app.Services.GetRequiredService<IImageRepository>();
    using var content = new System.IO.MemoryStream(Convert.FromBase64String(OneByOnePngBase64));
    await images.SaveAsync(imageId, content, "image/png", $"{imageId}.png", overwrite: true).ConfigureAwait(false);
  }

  private static object Image(string imageId) => new { type = "imageVisible", imageId };

  private static object SequenceTap(string stepId, object? condition = null) => condition is null
    ? new {
      stepId,
      primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 50, y = 50 } }
    }
    : (object)new {
      stepId,
      primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 50, y = 50 } },
      condition
    };

  private static object SequencePayload(string name, object[] steps, bool declare = true, int version = 1) => new {
    name,
    version,
    parameters = declare ? NovaDeclaration() : Array.Empty<object>(),
    steps
  };

  private static List<(string? Code, string? FieldPath, string? Name)> Warnings(JsonElement root) =>
      root.GetProperty("warnings").EnumerateArray()
        .Select(w => (w.GetProperty("code").GetString(), w.GetProperty("fieldPath").GetString(),
            w.GetProperty("parameterName").GetString()))
        .ToList();

  [Fact]
  public async Task SequenceWithAStepConditionPlaceholderIsCreatedWithWarnings() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var create = await client.PostAsJsonAsync("/api/sequences", SequencePayload(
        $"daily-option-{Guid.NewGuid():N}",
        new[] { SequenceTap("tap-option", Image("{{novaOption}}")) })).ConfigureAwait(false);

    var body = await create.Content.ReadAsStringAsync().ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created, body);
    var root = JsonDocument.Parse(body).RootElement;
    Warnings(root).Should().ContainSingle().Which.Should().Be(("static_check_skipped", "condition.imageId", "novaOption"));
    root.GetProperty("warnings")[0].GetProperty("message").GetString().Should().Be(
        "Step 'tap-option': 'condition.imageId' is parametrized, so its target is checked at run time instead of now.");
    var id = root.GetProperty("id").GetString();

    var get = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    var fetched = await ReadJsonAsync(get).ConfigureAwait(false);
    fetched.TryGetProperty("warnings", out _).Should().BeFalse("GET does not change");
    fetched.GetProperty("steps")[0].GetProperty("condition").GetProperty("imageId").GetString()
        .Should().Be("{{novaOption}}");

    var put = await client.PutAsJsonAsync($"/api/sequences/{id}", SequencePayload(
        "daily-option-put", new[] { SequenceTap("tap-option", Image("{{novaOption}}")) }, version: 1)).ConfigureAwait(false);
    var putBody = await put.Content.ReadAsStringAsync().ConfigureAwait(false);
    put.StatusCode.Should().Be(HttpStatusCode.OK, putBody);
    Warnings(JsonDocument.Parse(putBody).RootElement).Should().ContainSingle()
        .Which.Should().Be(("static_check_skipped", "condition.imageId", "novaOption"));

    var patch = await client.PatchAsJsonAsync($"/api/sequences/{id}", SequencePayload(
        "daily-option-patch", new[] { SequenceTap("tap-option", Image("{{novaOption}}")) }, version: 2)).ConfigureAwait(false);
    var patchBody = await patch.Content.ReadAsStringAsync().ConfigureAwait(false);
    patch.StatusCode.Should().Be(HttpStatusCode.OK, patchBody);
    Warnings(JsonDocument.Parse(patchBody).RootElement).Should().ContainSingle()
        .Which.Should().Be(("static_check_skipped", "condition.imageId", "novaOption"));
  }

  [Fact]
  public async Task PlaceholderIsAcceptedInEachConditionPosition() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    await RegisterImageAsync(app, "option-literal-114").ConfigureAwait(false);

    var steps = new object[] {
      new {
        stepId = "branch",
        stepType = "If",
        @if = new { condition = Image("{{novaOption}}") },
        body = new object[] { SequenceTap("then-step") }
      },
      new {
        stepId = "wait-loop",
        stepType = "Loop",
        loop = new { loopType = "while", condition = Image("img-{{iteration}}"), maxIterations = 3 },
        body = new object[] {
          SequenceTap("body-tap"),
          new { stepId = "brk", stepType = "Break", breakCondition = Image("{{novaOption}}") }
        }
      },
      new {
        stepId = "until-loop",
        stepType = "Loop",
        loop = new { loopType = "repeatUntil", condition = Image("{{novaOption}}"), maxIterations = 3 },
        body = new object[] { SequenceTap("until-tap") }
      },
      SequenceTap("gate", new {
        type = "all",
        children = new object[] { Image("option-literal-114"), Image("{{novaOption}}") }
      })
    };

    var response = await client.PostAsJsonAsync("/api/sequences",
        SequencePayload($"positions-{Guid.NewGuid():N}", steps)).ConfigureAwait(false);

    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    var warnings = Warnings(JsonDocument.Parse(body).RootElement);
    warnings.Should().OnlyContain(w => w.Code == "static_check_skipped");
    warnings.Select(w => w.FieldPath).Should().BeEquivalentTo(
        "if.condition.imageId",
        "loop.condition.imageId",
        "breakCondition.imageId",
        "loop.condition.imageId",
        "condition.children[1].imageId");
  }

  [Fact]
  public async Task UndeclaredNameInAConditionIsUnresolvable() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", SequencePayload(
        $"undeclared-{Guid.NewGuid():N}",
        new[] { SequenceTap("tap-option", Image("{{novaOption}}")) },
        declare: false)).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var root = await ReadJsonAsync(response).ConfigureAwait(false);
    root.GetProperty("error").GetString().Should().Be("unresolvable_parameter_reference");
    root.GetProperty("details").EnumerateArray().First().GetProperty("fieldPath").GetString()
        .Should().Be("condition.imageId");
  }

  [Fact]
  public async Task LiteralConditionImageIdKeepsTheExistenceCheck() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", SequencePayload(
        $"literal-{Guid.NewGuid():N}",
        new[] { SequenceTap("tap-option", Image("no-such-image-114")) })).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false))
        .Should().Contain("Image reference 'no-such-image-114' does not exist (used by: tap-option).");
  }

  [Fact]
  public async Task SequenceResponseWithNoWarningsHasNoWarningsMember() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    await RegisterImageAsync(app, "option-literal-114").ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", SequencePayload(
        $"no-warnings-{Guid.NewGuid():N}",
        new[] { SequenceTap("tap-option", Image("option-literal-114")) })).ConfigureAwait(false);

    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    JsonDocument.Parse(body).RootElement.TryGetProperty("warnings", out _).Should().BeFalse();
  }

  [Fact]
  public async Task DryRunResponseDoesNotChange() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = $"dry-{Guid.NewGuid():N}",
      version = 1,
      dryRun = true,
      parameters = NovaDeclaration(),
      steps = new[] { SequenceTap("tap-option", Image("{{novaOption}}")) }
    }).ConfigureAwait(false);

    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    var root = JsonDocument.Parse(body).RootElement;
    root.GetProperty("valid").GetBoolean().Should().BeTrue();
    root.TryGetProperty("warnings", out _).Should().BeFalse();
  }
}
