using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 168 (FR-023): the OpenAPI document describes every new declaration member.</summary>
public sealed class FriendlyParametersOpenApiTests {
  private static readonly string[] NewMembers = {
    "label", "helpText", "kind", "options", "allowedImages", "min", "max", "unit", "pattern"
  };

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

  private static JsonElement DeclarationSchema(JsonElement root) {
    var schemas = root.GetProperty("components").GetProperty("schemas");
    var items = schemas.GetProperty("CommandCreateSchema").GetProperty("properties").GetProperty("parameters").GetProperty("items");
    if (items.TryGetProperty("$ref", out var reference)) {
      return schemas.GetProperty(reference.GetString()!.Split('/').Last());
    }

    return items;
  }

  [Fact]
  public async Task DeclarationItemShowsEveryNewMemberWithADescription() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    var properties = DeclarationSchema(root).GetProperty("properties");
    foreach (var name in NewMembers) {
      properties.TryGetProperty(name, out var member).Should().BeTrue($"the item must show {name}");
      member.TryGetProperty("description", out var description).Should().BeTrue($"{name} needs a description");
      description.GetString().Should().NotBeNullOrWhiteSpace();
    }
  }

  [Fact]
  public async Task NewMembersAreOptional() {
    var root = await ReadDocumentAsync().ConfigureAwait(false);

    var schema = DeclarationSchema(root);
    if (schema.TryGetProperty("required", out var required)) {
      required.EnumerateArray().Select(e => e.GetString()).Should().NotContain(NewMembers);
    }
  }
}
