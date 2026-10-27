using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Swagger;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

/// <summary>
/// Feature 164: the OpenAPI document shows the rules for the key text of a key input step. The rules are the digit
/// names, the number pad names, the raw key code (0 to 300), and the priority of a name over a raw key code.
/// </summary>
public sealed class KeyInputOpenApiTests {
  private static async Task<(JsonElement Root, string Raw)> ReadDocumentAsync() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    using var document = JsonDocument.Parse(raw);
    return (document.RootElement.Clone(), raw);
  }

  [Fact]
  public async Task KeyInputConfigDocumentsTheKeyTextRules() {
    var (root, _) = await ReadDocumentAsync().ConfigureAwait(false);

    var key = root.GetProperty("components").GetProperty("schemas").GetProperty("KeyInputConfigDto")
      .GetProperty("properties").GetProperty("key");

    var description = key.GetProperty("description").GetString();
    description.Should().Contain("0 to 9").And.Contain("NUMPAD_0").And.Contain("NUMPAD_9");
    description.Should().Contain("0 to 300").And.Contain("raw Android key code");
    description.Should().Contain("priority").And.Contain("ignores case");
    description.Should().Contain("\"6\"").And.Contain("\"85\"");
    description.Should().Contain("not_accepted").And.Contain("key_input_unknown_key").And.Contain("key_input_code_out_of_range");
    key.GetProperty("example").GetString().Should().Be("6");
  }

  [Fact]
  public async Task PrimitiveActionPayloadDocumentsTheKeyTextRule() {
    var (root, _) = await ReadDocumentAsync().ConfigureAwait(false);

    var payload = root.GetProperty("components").GetProperty("schemas").GetProperty("PrimitiveActionRequest")
      .GetProperty("properties").GetProperty("payload").GetProperty("description").GetString();

    var keyLine = payload!.Split('\n').Single(line => line.StartsWith("- key:", StringComparison.Ordinal));
    keyLine.Should().Contain(KeyInputSchemaFilter.RuleSummary);
    keyLine.Should().Contain("keyCode");
  }

  [Fact]
  public async Task KeyActionDefinitionExampleShowsTheKeyTextRule() {
    var (_, raw) = await ReadDocumentAsync().ConfigureAwait(false);

    raw.Should().Contain(KeyInputSchemaFilter.RuleSummary);
    raw.Should().NotContain("\"allowedValues\"", "the key action example must not limit the key name to three values");
  }
}
