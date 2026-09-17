using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequencePerStepConditionsOpenApiTests {
  private static readonly string[] RequiredCommandOutcomeFields = { "stepRef", "expectedState" };
  private static readonly string[] CompositeRuleNames = { "all", "any", "none" };
  private static readonly string[] ExistingConditionKinds = { "imageVisible", "commandOutcome" };

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task SwaggerDocumentIncludesSequenceEndpointGroup() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var paths = document.RootElement.GetProperty("paths");

    paths.TryGetProperty("/api/sequences", out _).Should().BeTrue();
    paths.TryGetProperty("/api/sequences/{sequenceId}", out _).Should().BeTrue();
    paths.TryGetProperty("/api/sequences/{sequenceId}/execute", out _).Should().BeTrue();
  }

  [Fact]
  public async Task SwaggerDocumentDefinesPerStepConditionSchemasAndCommandOutcomeRequirements() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var root = document.RootElement;

    var schemas = root.GetProperty("components").GetProperty("schemas");

    schemas.TryGetProperty("SequenceStepCondition", out _).Should().BeTrue();
    schemas.TryGetProperty("ImageVisibleCondition", out _).Should().BeTrue();
    schemas.TryGetProperty("CommandOutcomeCondition", out _).Should().BeTrue();

    var commandOutcome = schemas.GetProperty("CommandOutcomeCondition");
    var properties = commandOutcome.GetProperty("properties");
    properties.TryGetProperty("stepRef", out _).Should().BeTrue();
    properties.TryGetProperty("expectedState", out _).Should().BeTrue();
    properties.GetProperty("stepRef").GetProperty("type").GetString().Should().Be("string");
    properties.GetProperty("expectedState").GetProperty("type").GetString().Should().Be("string");

    if (commandOutcome.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array) {
      var requiredFields = required.EnumerateArray().Select(entry => entry.GetString()).ToArray();
      requiredFields.Should().Contain(RequiredCommandOutcomeFields);
    }

    var sequenceStep = schemas.GetProperty("SequenceStep");
    var conditionProperty = sequenceStep.GetProperty("properties").GetProperty("condition");
    if (conditionProperty.TryGetProperty("$ref", out var conditionRef)) {
      conditionRef.GetString().Should().StartWith("#/components/schemas/SequenceStepCondition");
    }
    else {
      (conditionProperty.TryGetProperty("oneOf", out _) || conditionProperty.TryGetProperty("anyOf", out _)).Should().BeTrue();
    }

  }

  /// <summary>
  /// Feature 088, FR-014: the composite kinds must be discoverable from the published schema. A
  /// capability that exists only at run time is one an author finds by trial and error.
  /// </summary>
  [Theory]
  [InlineData("AllCondition")]
  [InlineData("AnyCondition")]
  [InlineData("NoneCondition")]
  public async Task SwaggerDocumentDefinesEachCompositeConditionSchema(string schemaName) {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

    schemas.TryGetProperty(schemaName, out var schema).Should().BeTrue();

    // The children array is what makes a composite composite, and it must be recursive — a child is
    // itself a condition, so nesting is expressible rather than being a single flat level.
    var children = FindChildrenProperty(schema, schemas);
    children.HasValue.Should().BeTrue("{0} must publish a children array", schemaName);
    children!.Value.GetProperty("type").GetString().Should().Be("array");
    children.Value.GetProperty("items").GetProperty("$ref").GetString()
      .Should().Contain("SequenceStepCondition");
  }

  [Fact]
  public async Task SwaggerDocumentExposesTheCompositeConditionDiscriminatorValues() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    using var document = JsonDocument.Parse(raw);
    var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

    schemas.TryGetProperty("CompositeCondition", out _).Should().BeTrue();

    var condition = schemas.GetProperty("SequenceStepCondition");
    if (condition.TryGetProperty("discriminator", out var discriminator)
        && discriminator.TryGetProperty("mapping", out var mapping)) {
      var keys = mapping.EnumerateObject().Select(p => p.Name).ToArray();
      keys.Should().Contain(CompositeRuleNames);
      keys.Should().Contain(ExistingConditionKinds, "the existing kinds must survive");
    }
    else {
      // No discriminator block published: the rule names must still appear in the document, or an
      // author has no way to learn them without reading source.
      raw.Should().Contain("AllCondition").And.Contain("AnyCondition").And.Contain("NoneCondition");
    }
  }

  /// <summary>
  /// Feature 103, FR-014/FR-015: the reference rules and the loop exit reason must be legible from
  /// the published document.
  /// <para>
  /// Issue #193 exists because a delivered behaviour was recorded in a claim nobody re-checked. A
  /// description that drifts is the same failure in a different place, so these assertions name the
  /// specific phrases rather than merely checking that some description exists.
  /// </para>
  /// </summary>
  [Fact]
  public async Task SwaggerDocumentPublishesTheCommandOutcomeReferenceRules() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var commandOutcome = document.RootElement
      .GetProperty("components").GetProperty("schemas").GetProperty("CommandOutcomeCondition");

    var stepRefDescription = commandOutcome.GetProperty("properties").GetProperty("stepRef")
      .GetProperty("description").GetString();

    stepRefDescription.Should().NotBeNullOrWhiteSpace();
    stepRefDescription.Should().Contain("reachable from the sequence root", "the resolution scope must be stated");
    stepRefDescription.Should().Contain("Loop body", "nested bodies are in scope and that must be explicit");
    stepRefDescription.Should().Contain("authored", "the ordering rule is authored order, not list index");
    stepRefDescription.Should().Contain("$.children", "a nested rejection names the offending child by path");
    stepRefDescription.Should().Contain("repeatUntil", "the inherited D-006 exception must be stated, not hidden");

    var expectedStateDescription = commandOutcome.GetProperty("properties").GetProperty("expectedState")
      .GetProperty("description").GetString();

    // All five, named. The reported ceiling was that only the first three existed.
    expectedStateDescription.Should().NotBeNullOrWhiteSpace();
    foreach (var state in new[] { "success", "failed", "skipped", "break", "no_break" }) {
      expectedStateDescription.Should().Contain(state);
    }
  }

  [Fact]
  public async Task SwaggerDocumentPublishesTheLoopExitReasonShape() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    // The run response is the domain result serialized directly, so there is no contract schema to
    // hang this on; it is described on the execution-log entry, which is where a consumer asking
    // "why did that loop stop" actually looks.
    raw.Should().Contain("brokeVia");
    raw.Should().Contain("exhaustedMaxIterations");
    raw.Should().Contain("mutually exclusive", "the two causes cannot both be reported");
    raw.Should().Contain("never an enclosing If", "brokeVia names the Break's own id");
  }

  /// <summary>
  /// Finds the <c>children</c> property on a schema, following a single <c>allOf</c> hop, since a
  /// derived schema commonly inherits it from the composite base rather than redeclaring it.
  /// </summary>
  private static JsonElement? FindChildrenProperty(JsonElement schema, JsonElement schemas) {
    if (schema.TryGetProperty("properties", out var properties)
        && properties.TryGetProperty("children", out var direct)) {
      return direct;
    }

    if (!schema.TryGetProperty("allOf", out var allOf) || allOf.ValueKind != JsonValueKind.Array) {
      return null;
    }

    foreach (var entry in allOf.EnumerateArray()) {
      if (entry.TryGetProperty("properties", out var entryProperties)
          && entryProperties.TryGetProperty("children", out var nested)) {
        return nested;
      }

      if (!entry.TryGetProperty("$ref", out var reference)) {
        continue;
      }

      var name = reference.GetString()?.Split('/')[^1];
      if (name is not null
          && schemas.TryGetProperty(name, out var referenced)
          && referenced.TryGetProperty("properties", out var referencedProperties)
          && referencedProperties.TryGetProperty("children", out var fromBase)) {
        return fromBase;
      }
    }

    return null;
  }
}
