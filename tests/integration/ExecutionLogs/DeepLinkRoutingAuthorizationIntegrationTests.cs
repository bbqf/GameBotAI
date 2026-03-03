using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class DeepLinkRoutingAuthorizationIntegrationTests : IDisposable {
  private readonly string? _prevAuthToken;

  public DeepLinkRoutingAuthorizationIntegrationTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task DetailEndpointAuthBehaviorIsUnaffectedByDeepLinkQueryHints() {
    using var app = new WebApplicationFactory<Program>();
    using var unauthenticated = app.CreateClient();

    var unauthorized = await unauthenticated.GetAsync(new Uri("/api/execution-logs/some-id?sequenceId=seq-1&stepId=step-1", UriKind.Relative)).ConfigureAwait(false);
    unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    using var authenticated = app.CreateClient();
    authenticated.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var notFound = await authenticated.GetAsync(new Uri("/api/execution-logs/some-id?sequenceId=seq-1&stepId=step-1", UriKind.Relative)).ConfigureAwait(false);
    notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }
}
