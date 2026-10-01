using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 122: the OpenAPI document shows the option that hides success notifications.
/// </summary>
public sealed class ExcludeFromSuccessNotificationsOpenApiTests {
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

  [Theory]
  [InlineData("SequenceUpsertRequest")]
  [InlineData("SequencePatchContract")]
  public async Task TheMemberIsABooleanWithADescription(string schema) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var member = document.GetProperty("components").GetProperty("schemas").GetProperty(schema)
      .GetProperty("properties").GetProperty("excludeFromSuccessNotifications");

    member.GetProperty("type").GetString().Should().Be("boolean");
    member.GetProperty("description").GetString().Should().Contain("success").And.Contain("recovered");
  }
}
