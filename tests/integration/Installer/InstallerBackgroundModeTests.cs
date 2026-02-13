using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class InstallerBackgroundModeTests {
  [Fact]
  public async Task PreflightInBackgroundModeDoesNotRequireElevationAndUsesLoginStartupByDefault() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var payload = new {
      installMode = "backgroundApp",
      backendPort = 5000,
      requestedWebUiPort = 8080,
      protocol = "http",
      unattended = true,
      startOnLogin = true,
      confirmFirewallFallback = true
    };

    var resp = await client.PostAsJsonAsync("/api/installer/preflight", payload).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    body.Should().NotBeNull();
    var responseBody = body!;
    responseBody["requiresElevation"]?.ToString().Should().Be("False");
    responseBody["startupPolicy"]?.ToString().Should().Be("loginStartWhenEnabled");
  }
}
