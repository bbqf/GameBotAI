using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Images;

/// <summary>
/// Feature 101 (issue #188): <c>POST /api/images/detect</c> reports match coordinates as fractions of the
/// capture frame while <c>POST /api/images/detect-all</c> reports the same box in pixels, under the same field
/// names. The OpenAPI document must say which unit each route uses, on every coordinate field and on both
/// operations, so a caller never has to measure a known landmark to find out.
/// </summary>
public sealed class ImageDetectCoordinateUnitsOpenApiTests {
  private const string Detect = "/api/images/detect";
  private const string DetectAll = "/api/images/detect-all";

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

  /// <summary>Follows a <c>$ref</c> (directly or as the single <c>allOf</c> entry) to its component schema.</summary>
  private static JsonElement ResolveRef(JsonElement document, JsonElement element) {
    if (element.TryGetProperty("$ref", out var reference)) {
      var name = reference.GetString()!.Split('/').Last();
      return document.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }
    if (element.TryGetProperty("allOf", out var allOf) && allOf.GetArrayLength() == 1) {
      return ResolveRef(document, allOf[0]);
    }
    return element;
  }

  private static JsonElement Operation(JsonElement document, string path)
    => document.GetProperty("paths").GetProperty(path).GetProperty("post");

  private static JsonElement OkContent(JsonElement document, string path)
    => Operation(document, path).GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json");

  /// <summary>The component schema of one item of the route's 200 <c>matches</c> array.</summary>
  private static JsonElement MatchSchema(JsonElement document, string path) {
    var response = ResolveRef(document, OkContent(document, path).GetProperty("schema"));
    return ResolveRef(document, response.GetProperty("properties").GetProperty("matches").GetProperty("items"));
  }

  private static JsonElement Property(JsonElement schema, string name)
    => schema.GetProperty("properties").GetProperty(name);

  private static string Description(JsonElement element)
    => element.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty;

  private static JsonElement ExampleMatch(JsonElement document, string path)
    => OkContent(document, path).GetProperty("example").GetProperty("matches")[0];

  private static void ShouldDescribeFraction(JsonElement schema, string field, string dimension) {
    var description = Description(Property(schema, field));
    description.Should().Contain("fraction").And.Contain(dimension).And.Contain("not pixels").And.Contain("clamped");
    if (field == "x") description.Should().Contain("left");
    if (field == "y") description.Should().Contain("top");
  }

  [Theory]
  [InlineData("x", "width")]
  [InlineData("width", "width")]
  [InlineData("y", "height")]
  [InlineData("height", "height")]
  public async Task DetectMatchCoordinatesAreFractions(string field, string dimension) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    ShouldDescribeFraction(MatchSchema(document, Detect), field, dimension);
  }

  [Theory]
  [InlineData("x", "width")]
  [InlineData("width", "width")]
  [InlineData("y", "height")]
  [InlineData("height", "height")]
  public async Task DetectBboxCoordinatesAreFractions(string field, string dimension) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var bbox = ResolveRef(document, Property(MatchSchema(document, Detect), "bbox"));
    ShouldDescribeFraction(bbox, field, dimension);
  }

  [Fact]
  public async Task DetectBboxSaysItRepeatsTopLevel() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(Property(MatchSchema(document, Detect), "bbox")).Should().Contain("x/y/width/height");
  }

  [Fact]
  public async Task DetectOperationStatesUnits() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(Operation(document, Detect))
      .Should().Contain("fraction").And.Contain("pixels").And.Contain("detect-all")
      .And.Contain("matchedReferenceId").And.Contain("captureId");
  }

  [Fact]
  public async Task DetectExampleUsesFractions() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var match = ExampleMatch(document, Detect);
    foreach (var element in new[] { match, match.GetProperty("bbox") }) {
      foreach (var field in new[] { "x", "y", "width", "height" }) {
        element.GetProperty(field).GetDouble().Should().BeInRange(0, 1, "detect example {0} must be a fraction", field);
      }
    }
  }

  [Theory]
  [InlineData("x")]
  [InlineData("y")]
  [InlineData("width")]
  [InlineData("height")]
  public async Task DetectAllMatchCoordinatesArePixels(string field) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var description = Description(Property(MatchSchema(document, DetectAll), field));
    description.Should().Contain("pixels").And.Contain("not fractions");
    if (field == "x") description.Should().Contain("left");
    if (field == "y") description.Should().Contain("top");
  }

  [Fact]
  public async Task DetectAllOperationStatesUnits() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(Operation(document, DetectAll))
      .Should().Contain("pixels").And.Contain("fraction").And.Contain(Detect);
  }

  [Fact]
  public async Task DetectAllExampleUsesPixels() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var match = ExampleMatch(document, DetectAll);
    foreach (var field in new[] { "x", "y", "width", "height" }) {
      match.GetProperty(field).TryGetInt32(out _).Should().BeTrue("detect-all example {0} must be whole pixels", field);
    }
  }

  [Fact]
  public async Task IdentifierFieldsNameTheirCounterpart() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(Property(MatchSchema(document, Detect), "templateId"))
      .Should().Contain("imageId").And.Contain("detect-all");
    Description(Property(MatchSchema(document, DetectAll), "imageId"))
      .Should().Contain("templateId").And.Contain(Detect);
  }
}
