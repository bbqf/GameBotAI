using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequencePerStepConditionsOpenApiTests {
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
}
