using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class SessionsContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public SessionsContractTests() {
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
  public async Task CreateGetSnapshotDeleteFlowIsExposed() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Create session
    var createResp = await client.PostAsJsonAsync("/api/sessions", new { gameId = "test-game" }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    created.Should().NotBeNull();
    var id = created!["id"].ToString();
    id.Should().NotBeNullOrWhiteSpace();

    // Get session
    var getResp = await client.GetAsync(new Uri($"/api/sessions/{id}", UriKind.Relative)).ConfigureAwait(true);
    getResp.StatusCode.Should().Be(HttpStatusCode.OK);

    // Snapshot
    var snapResp = await client.GetAsync(new Uri($"/api/sessions/{id}/snapshot", UriKind.Relative)).ConfigureAwait(true);
    snapResp.StatusCode.Should().Be(HttpStatusCode.OK);
    snapResp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

    // Delete
    var delResp = await client.DeleteAsync(new Uri($"/api/sessions/{id}", UriKind.Relative)).ConfigureAwait(true);
    delResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
  }
}
