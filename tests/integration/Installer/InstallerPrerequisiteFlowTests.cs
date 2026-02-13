using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class InstallerPrerequisiteFlowTests {
  [Fact]
  public async Task ExecuteInstallReturnsSuccessWhenPrerequisitesDetected() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var payload = new {
      installMode = "backgroundApp",
      backendPort = 5000,
      requestedWebUiPort = 8080,
      protocol = "http",
      unattended = true
    };

    var resp = await client.PostAsJsonAsync("/api/installer/execute", payload).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
