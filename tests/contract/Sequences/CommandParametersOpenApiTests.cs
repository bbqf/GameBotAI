using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 138 (issue #247): the OpenAPI document shows the command <c>parameters</c> list
/// and the rule that you must declare a placeholder.
/// </summary>
public sealed class CommandParametersOpenApiTests {
  private static readonly string[] ItemProperties = { "name", "type", "default", "required", "description" };

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

  private static JsonElement Schema(JsonElement root, string name) =>
      root.GetProperty("components").GetProperty("schemas").GetProperty(name);

  private static JsonElement Resolve(JsonElement root, JsonElement node) {
    if (node.TryGetProperty("$ref", out var reference)) {
      var name = reference.GetString()!.Split('/').Last();
      return Schema(root, name);
    }
    return node;
  }

  private static JsonElement ParametersProperty(JsonElement root, string schema) {
    var properties = Schema(root, schema).GetProperty("properties");
    properties.TryGetProperty("parameters", out var parameters).Should().BeTrue($"{schema} must show parameters");
    return parameters;
  }

  private static JsonElement ItemSchema(JsonElement root, string schema) {
    var parameters = ParametersProperty(root, schema);
    parameters.GetProperty("type").GetString().Should().Be("array");
    return Resolve(root, parameters.GetProperty("items"));
  }

  [Fact]
  public async Task CreateSchemaShowsOptionalParametersList() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    var parameters = ParametersProperty(root, "CommandCreateSchema");
    parameters.GetProperty("type").GetString().Should().Be("array");
    var schema = Schema(root, "CommandCreateSchema");
    if (schema.TryGetProperty("required", out var required)) {
      required.EnumerateArray().Select(e => e.GetString()).Should().NotContain("parameters");
    }
  }

  [Fact]
  public async Task CreateParameterItemShowsFivePropertiesAndTypeValues() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    var item = ItemSchema(root, "CommandCreateSchema");
    var properties = item.GetProperty("properties");
    foreach (var name in ItemProperties) {
      properties.TryGetProperty(name, out _).Should().BeTrue($"item must show {name}");
    }
    if (item.TryGetProperty("required", out var required)) {
      required.EnumerateArray().Select(e => e.GetString()).Should().NotContain("default");
    }
    var type = properties.GetProperty("type");
    var text = type.GetRawText();
    text.Should().Contain("text").And.Contain("number");
  }

  [Fact]
  public async Task FieldTemplatesDescribesTheDeclarationRule() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    var description = Schema(root, "CommandStepDto").GetProperty("properties").GetProperty("fieldTemplates")
        .GetProperty("description").GetString() ?? string.Empty;

    description.Should().Contain("top-level `parameters` list")
        .And.Contain("queue built-in")
        .And.Contain("parameterBindings")
        .And.Contain("unresolvable_parameter_reference");
  }

  [Theory]
  [InlineData("UpdateCommandRequest")]
  [InlineData("CommandResponse")]
  public async Task CommandSchemasShowParameters(string schema) {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    ParametersProperty(root, schema).GetProperty("type").GetString().Should().Be("array");
  }

  [Fact]
  public async Task CreateExampleDeclaresEveryPlaceholder() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    var example = root.GetProperty("paths").GetProperty("/api/commands").GetProperty("post")
        .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
    example.TryGetProperty("parameters", out var parameters).Should().BeTrue("the example must show parameters");
    var declared = parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
    declared.Should().NotBeEmpty();

    var placeholders = Regex.Matches(example.GetRawText(), @"\{\{(\w+)\}\}").Select(m => m.Groups[1].Value).Distinct().ToList();
    placeholders.Should().NotBeEmpty();
    placeholders.Should().OnlyContain(p => declared.Contains(p));
  }
}
