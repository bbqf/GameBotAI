using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA1707

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Issue #335: the OpenAPI text of the <c>notify</c> action names the enabled notification targets as
/// the default destination.
/// </summary>
public sealed class NotifyActionOpenApiTextTests {
  [Fact]
  public async Task NotifyText_NamesTheEnabledNotificationTargetsAsDefault() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    json.Should().Contain("Without url, the message goes to the enabled notification targets");
    json.Should().Contain("With url, the message goes only to that URL");
    json.Should().Contain("Service:Notifications:DefaultUrl");
  }
}
