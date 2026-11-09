using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 179: the OpenAPI document describes the list members.</summary>
public sealed class ParameterListOpenApiTests {
  private static async Task<JsonElement> ReadDocumentAsync() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.Clone();
  }

  private static JsonElement Resolve(JsonElement schemas, JsonElement node) =>
      node.TryGetProperty("$ref", out var reference)
          ? schemas.GetProperty(reference.GetString()!.Split('/').Last())
          : node;

  [Fact]
  public async Task DeclarationShowsMultipleMinItemsAndMaxItems() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);
    var schemas = root.GetProperty("components").GetProperty("schemas");
    var items = schemas.GetProperty("CommandCreateSchema").GetProperty("properties").GetProperty("parameters").GetProperty("items");
    var properties = Resolve(schemas, items).GetProperty("properties");

    foreach (var name in new[] { "multiple", "minItems", "maxItems" }) {
      properties.TryGetProperty(name, out var member).Should().BeTrue($"the declaration must show {name}");
      member.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }
  }

  [Fact]
  public async Task BindingShowsValues() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);
    var schemas = root.GetProperty("components").GetProperty("schemas");
    var binding = schemas.EnumerateObject()
        .Where(s => string.Equals(s.Name, "ParameterBindingDto", StringComparison.Ordinal))
        .Select(s => s.Value)
        .FirstOrDefault(s => s.TryGetProperty("properties", out var p) && p.TryGetProperty("values", out _));

    binding.ValueKind.Should().NotBe(JsonValueKind.Undefined, "a binding schema must show values");
    binding.GetProperty("properties").GetProperty("values").GetProperty("description").GetString()
        .Should().NotBeNullOrWhiteSpace();
  }
}
