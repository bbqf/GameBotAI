using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Issue #242 (feature 113, FR-008): the description of <c>POST /api/sequences</c> must tell that
/// <c>dryRun</c> applies to each body shape and that a malformed step gets a 400 that names the step.
/// </summary>
public sealed class SequenceCreateMalformedStepContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task CreateDescriptionTellsDryRunAndMalformedStepRules() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

    var description = document.RootElement.GetProperty("paths").GetProperty("/api/sequences")
      .GetProperty("post").GetProperty("description").GetString();

    description.Should().Contain("dryRun: true (any body shape)")
      .And.Contain("steps[<index>] (stepId '<id>')")
      .And.Contain("400");
  }
}
