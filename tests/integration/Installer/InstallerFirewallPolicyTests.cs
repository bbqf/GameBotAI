using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class InstallerFirewallPolicyTests {
  [Fact]
  public async Task PreflightBackgroundModeReturnsWarningWhenHostDefaultFirewallIsUsed() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var payload = new {
      installMode = "backgroundApp",
      backendPort = 5000,
      requestedWebUiPort = 8080,
      protocol = "http",
      unattended = true,
      confirmFirewallFallback = true
    };

    var resp = await client.PostAsJsonAsync("/api/installer/preflight", payload).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    body.Should().NotBeNull();
    var responseBody = body!;
    responseBody["firewallScope"]?.ToString().Should().Be("hostDefault");
    responseBody["warnings"]?.ToString().Should().ContainEquivalentOf("firewall");
  }

  [Fact]
  public async Task PreflightBackgroundModeRejectsWhenFallbackNotConfirmed() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var payload = new {
      installMode = "backgroundApp",
      backendPort = 5000,
      requestedWebUiPort = 8080,
      protocol = "http",
      unattended = true,
      confirmFirewallFallback = false
    };

    var resp = await client.PostAsJsonAsync("/api/installer/preflight", payload).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }
}
