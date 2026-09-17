using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Actions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 102 (issue #201): the sequence step validator accepts eleven <c>primitiveAction.type</c> values, but the
/// OpenAPI document published <c>type</c> as a free string and <c>payload</c> as an undescribed object, so
/// <c>reschedule-self</c> could only be found by probing validator errors. The document must enumerate the same list
/// the validator uses and describe every type's payload.
/// </summary>
public sealed class PrimitiveActionTypesOpenApiTests {
  private static readonly string[] IssueNamedTypes = { "reschedule-self", "tap", "swipe", "key", "ensure-game-running", "notify" };

  private static readonly string[] RescheduleSelfStatements = {
    "option", "AtQueueStart", "OncePerRun", "Timer", "EveryStep", "timerTimeOfDay", "timerRelativeOffset",
    "24:00:00", "exactly one", "ocrOffset", "region", "fallback", "min", "max", "any other option", "queue"
  };

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<JsonElement> ReadDocumentAsync() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.Clone();
  }

  private static JsonElement Schemas(JsonElement document)
    => document.GetProperty("components").GetProperty("schemas");

  private static JsonElement PrimitiveActionSchema(JsonElement document)
    => Schemas(document).GetProperty("PrimitiveAction");

  /// <summary>Follows a <c>$ref</c> (directly or as the single <c>allOf</c> entry) to its component schema.</summary>
  private static JsonElement ResolveRef(JsonElement document, JsonElement element) {
    if (element.TryGetProperty("$ref", out var reference)) {
      return Schemas(document).GetProperty(reference.GetString()!.Split('/').Last());
    }
    if (element.TryGetProperty("allOf", out var allOf) && allOf.GetArrayLength() == 1) {
      return ResolveRef(document, allOf[0]);
    }
    return element;
  }

  private static JsonElement Property(JsonElement schema, string name)
    => schema.GetProperty("properties").GetProperty(name);

  private static string Description(JsonElement element)
    => element.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty;

  /// <summary>
  /// The text of one type's section of the payload description: from its <c>"&lt;type&gt;:"</c> marker up to the
  /// next marker of any other supported type, or the end of the description.
  /// </summary>
  private static string PayloadSection(string description, string type) {
    var marker = type + ":";
    var start = description.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0) return string.Empty;
    var end = SequenceActionTypes.All
      .Where(other => !string.Equals(other, type, StringComparison.Ordinal))
      .Select(other => description.IndexOf(other + ":", start + marker.Length, StringComparison.Ordinal))
      .Where(index => index >= 0)
      .DefaultIfEmpty(description.Length)
      .Min();
    return description[start..end];
  }

  private static string[] EnumValues(JsonElement typeProperty)
    => typeProperty.TryGetProperty("enum", out var values)
      ? values.EnumerateArray().Select(v => v.GetString()!).ToArray()
      : Array.Empty<string>();

  [Fact]
  public async Task TypeEnumMatchesValidatorList() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var values = EnumValues(Property(PrimitiveActionSchema(document), "type"));

    values.Should().Equal(SequenceActionTypes.All);
    values.Should().HaveCount(11);
  }

  [Fact]
  public async Task TypeEnumIncludesIssueNamedTypes() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    EnumValues(Property(PrimitiveActionSchema(document), "type"))
      .Should().Contain(IssueNamedTypes);
  }

  [Fact]
  public async Task TypeDescriptionStatesMatchingAndSessionRule() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(Property(PrimitiveActionSchema(document), "type"))
      .Should().Contain("case-insensitive").And.Contain("connect-to-game").And.Contain("/api/sessions/start");
  }

  [Fact]
  public async Task SessionStartRequestUsesSameSchema() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var primitiveAction = ResolveRef(document, Property(Schemas(document).GetProperty("StartSessionRequest"), "primitiveAction"));

    EnumValues(Property(primitiveAction, "type")).Should().Equal(SequenceActionTypes.All);
  }

  public static TheoryData<string> SupportedTypes() {
    var data = new TheoryData<string>();
    foreach (var type in SequenceActionTypes.All) data.Add(type);
    return data;
  }

  private static async Task<string> PayloadDescriptionAsync() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);
    return Description(Property(PrimitiveActionSchema(document), "payload"));
  }

  [Theory]
  [MemberData(nameof(SupportedTypes))]
  public async Task PayloadDescriptionCoversEveryType(string type) {
    ArgumentNullException.ThrowIfNull(type);
    var description = await PayloadDescriptionAsync().ConfigureAwait(false);

    description.Should().Contain(type + ":");
    PayloadSection(description, type).Trim().Length.Should().BeGreaterThan(type.Length + 1);
  }

  [Fact]
  public void PayloadDescriptionsMapCoversValidatorList() {
    GameBot.Service.Swagger.PrimitiveActionSchemaFilter.PayloadDescriptions.Keys
      .Should().BeEquivalentTo(SequenceActionTypes.All);
  }

  [Fact]
  public async Task RescheduleSelfPayloadIsFullyDescribed() {
    var section = PayloadSection(await PayloadDescriptionAsync().ConfigureAwait(false), "reschedule-self");

    foreach (var expected in RescheduleSelfStatements) {
      section.Should().Contain(expected);
    }
  }

  [Theory]
  [InlineData("tap", "x", "y")]
  [InlineData("swipe", "x1", "y1", "x2", "y2", "durationMs")]
  [InlineData("key", "keyCode")]
  [InlineData("command", "commandId")]
  [InlineData("notify", "message")]
  [InlineData("ensure-emulator-running", "adbSerial", "instanceName")]
  [InlineData("connect-to-game", "gameId", "adbSerial")]
  [InlineData("WaitForImage", "detectionTarget", "timeoutMs")]
  [InlineData("ensure-game-running", "no payload fields")]
  [InlineData("go-to-home-screen", "no payload fields")]
  public async Task InputPayloadsNameTheirFields(string type, params string[] fields) {
    ArgumentNullException.ThrowIfNull(fields);
    var section = PayloadSection(await PayloadDescriptionAsync().ConfigureAwait(false), type);

    foreach (var field in fields) {
      section.Should().Contain(field, "the {0} payload description must name {1}", type, field);
    }
  }

  [Fact]
  public async Task SequenceCreateExampleShowsRescheduleSelf() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var steps = document.GetProperty("paths").GetProperty("/api/sequences").GetProperty("post")
      .GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
      .GetProperty("example").GetProperty("steps");

    steps.EnumerateArray().Any(IsRescheduleSelfTimerStep).Should().BeTrue(
      "the sequence create example must show a reschedule-self Timer step");
  }

  [Fact]
  public async Task DryRunCreateWithUnsupportedTypeListsSupportedValues() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "primitive-action-types-probe",
      version = 1,
      dryRun = true,
      steps = new[] {
        new { stepId = "a", stepType = "Action", primitiveAction = new { type = "bogus", schemaVersion = "v1", payload = new { } } }
      }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!)
      .Should().Contain(e =>
        e.StartsWith("Step 'a' action type 'bogus' is not a supported primitive action type (expected one of ", StringComparison.Ordinal)
        && e.Contains("reschedule-self", StringComparison.Ordinal));
  }

  private static bool IsRescheduleSelfTimerStep(JsonElement step) {
    if (!step.TryGetProperty("primitiveAction", out var action)) return false;
    return action.GetProperty("type").GetString() == "reschedule-self"
      && action.GetProperty("payload").GetProperty("option").GetString() == "Timer"
      && action.GetProperty("payload").GetProperty("timerRelativeOffset").GetString() == "00:30:00";
  }
}
