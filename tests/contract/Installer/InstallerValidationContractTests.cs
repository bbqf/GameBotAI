using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class InstallerValidationContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public InstallerValidationContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task PreflightReturnsBadRequestForInvalidMode() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      installMode = "invalid",
      backendPort = 5000,
      requestedWebUiPort = 8080,
      protocol = "http",
      unattended = true
    };

    var resp = await client.PostAsJsonAsync("/api/installer/preflight", payload).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task ExecuteReturnsBadRequestForConflictingPorts() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      installMode = "backgroundApp",
      backendPort = 0,
      requestedWebUiPort = 8080,
      protocol = "http",
      unattended = true
    };

    var resp = await client.PostAsJsonAsync("/api/installer/execute", payload).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }
}
