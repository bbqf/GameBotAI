using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Actions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 125: the published description of the <c>reschedule-self</c> payload names the <c>keep</c>
/// option, its only value, the Timer-only rule, and the default behavior (FR-009).
/// </summary>
public sealed class SelfRescheduleKeepOpenApiContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<string> RescheduleSelfSectionAsync() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var description = document.RootElement.GetProperty("components").GetProperty("schemas")
      .GetProperty("PrimitiveAction").GetProperty("properties").GetProperty("payload")
      .GetProperty("description").GetString() ?? string.Empty;

    const string marker = "reschedule-self:";
    var start = description.IndexOf(marker, StringComparison.Ordinal);
    start.Should().BeGreaterThanOrEqualTo(0);
    var end = SequenceActionTypes.All
      .Where(other => !string.Equals(other, "reschedule-self", StringComparison.Ordinal))
      .Select(other => description.IndexOf(other + ":", start + marker.Length, StringComparison.Ordinal))
      .Where(index => index >= 0)
      .DefaultIfEmpty(description.Length)
      .Min();
    return description[start..end];
  }

  [Fact]
  public async Task PayloadDescriptionNamesKeepAndEarliest() {
    var section = await RescheduleSelfSectionAsync().ConfigureAwait(false);

    section.Should().Contain("keep").And.Contain("earliest");
  }

  [Fact]
  public async Task PayloadDescriptionStatesTheTimerOnlyTheSameRunAndTheDefaultRules() {
    var section = await RescheduleSelfSectionAsync().ConfigureAwait(false);

    section.Should().Contain("keep is only valid when option is Timer");
    section.Should().Contain("same run");
    section.Should().Contain("Without keep, the last booking wins");
  }
}
