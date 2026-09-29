using System;
using System.Threading.Tasks;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.QueueTemplates;

/// <summary>
/// Issue #226: the OpenAPI text of a template entry's <c>timerTimeOfDay</c> states the one accepted
/// format, the same as the text of the <c>reschedule-self</c> payload.
/// </summary>
public sealed class TemplateTimerTimeOfDayOpenApiTests {
  private const string FormatPhrase = "HH:mm or HH:mm:ss";

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

  [Theory]
  [InlineData("TemplateEntrySaveRequest")]
  [InlineData("QueueTemplateEntryResponse")]
  public async Task TemplateEntryTimerTimeOfDayNamesTheAcceptedFormat(string schemaName) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var property = document.GetProperty("components").GetProperty("schemas").GetProperty(schemaName)
      .GetProperty("properties").GetProperty("timerTimeOfDay");

    property.TryGetProperty("description", out var description).Should().BeTrue();
    description.GetString().Should().Contain(FormatPhrase);
  }
}
