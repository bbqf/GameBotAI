using System.Net;
using FluentAssertions;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionLogsAuthorizationIntegrationTests : IDisposable {
  private readonly string? _prevAuthToken;

  public ExecutionLogsAuthorizationIntegrationTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task ExecutionLogsEndpointsRequireAuthWhenTokenConfigured() {
    using var app = new WebApplicationFactory<Program>();
    using var client = app.CreateClient();

    var listResponse = await client.GetAsync(new Uri("/api/execution-logs", UriKind.Relative)).ConfigureAwait(true);
    listResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    var detailResponse = await client.GetAsync(new Uri("/api/execution-logs/nonexistent", UriKind.Relative)).ConfigureAwait(true);
    detailResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task ExecutionLogsEndpointsAllowAccessWithValidBearerToken() {
    using var app = new WebApplicationFactory<Program>();
    using var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var service = app.Services.GetRequiredService<IExecutionLogService>();
    await service.LogCommandExecutionAsync(
      "cmd-auth-001",
      "Authorization Command",
      "success",
      Array.Empty<PrimitiveTapStepOutcome>(),
      new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

    var listResponse = await client.GetAsync(new Uri("/api/execution-logs", UriKind.Relative)).ConfigureAwait(true);
    listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
