using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

/// <summary>
/// Feature 192: the OpenAPI document shows the TextInput step type, the text rules, the reason codes, the
/// typed length of a step result, the textInput.text key of fieldTemplates, and dryRun on the command routes.
/// </summary>
public sealed class TextInputOpenApiTests {
  private static async Task<JsonElement> ReadDocumentAsync() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    using var document = JsonDocument.Parse(raw);
    return document.RootElement.Clone();
  }

  private static JsonElement Schema(JsonElement root, string name) =>
    root.GetProperty("components").GetProperty("schemas").GetProperty(name);

  [Fact]
  public async Task TheStepTypeListHasTextInput() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    Schema(root, "CommandStepTypeDto").GetProperty("enum").EnumerateArray()
      .Select(e => e.GetString()).Should().Contain("TextInput");
  }

  [Fact]
  public async Task TheTextIsRequiredAndDocumentsTheRules() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);
    var config = Schema(root, "TextInputConfigDto");

    config.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Contain("text");
    var text = config.GetProperty("properties").GetProperty("text");
    var description = text.GetProperty("description").GetString();
    description.Should().Contain("U+0020").And.Contain("U+007E").And.Contain("500").And.Contain("fieldTemplates");
    description.Should().Contain("text_input_empty_text").And.Contain("text_input_too_long")
      .And.Contain("text_input_unsupported_character").And.Contain("text_input_not_accepted")
      .And.Contain("text_input_missing_config");
    text.GetProperty("example").GetString().Should().Be("250");
  }

  [Fact]
  public async Task TheStepResultDocumentsTheTypedLength() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    Schema(root, "StepExecutionOutcomeDto").GetProperty("properties").GetProperty("typedLength")
      .GetProperty("description").GetString().Should().Contain("TextInput");
  }

  [Fact]
  public async Task TheFieldTemplatesTextNamesTheTextKey() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    Schema(root, "CommandStepDto").GetProperty("properties").GetProperty("fieldTemplates")
      .GetProperty("description").GetString().Should().Contain("textInput.text");
  }

  [Fact]
  public async Task TheCommandWriteRoutesDocumentDryRun() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);
    var paths = root.GetProperty("paths");

    paths.GetProperty("/api/commands").GetProperty("post").GetProperty("description").GetString()
      .Should().Contain("dryRun").And.Contain("valid: true");
    paths.GetProperty("/api/commands/{id}").GetProperty("patch").GetProperty("description").GetString()
      .Should().Contain("dryRun").And.Contain("valid: true");
  }
}
