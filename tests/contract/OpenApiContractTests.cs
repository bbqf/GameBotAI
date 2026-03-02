using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class OpenApiContractTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynPort;
  private readonly string? _prevToken;
  public OpenApiContractTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task SwaggerDocumentContainsHealthEndpoint() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true);
    resp.EnsureSuccessStatusCode();
    var doc = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    doc.Should().NotBeNull();
    doc!.ContainsKey("paths").Should().BeTrue();
    var pathsElement = (System.Text.Json.JsonElement)doc["paths"];
    pathsElement.ToString().Should().Contain("/health");
  }

  [Fact]
  public async Task SwaggerDocumentIncludesPrimitiveTapCommandContracts() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true);
    resp.EnsureSuccessStatusCode();

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync()).ConfigureAwait(true);
    var root = doc.RootElement;

    root.ToString().Should().Contain("PrimitiveTap");

    root.ToString().Should().Contain("evaluate-and-execute");
  }

  [Fact]
  public async Task SwaggerDocumentIncludesExecutionLogRetentionEndpoints() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true);
    resp.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
    var root = doc.RootElement;
    var paths = root.GetProperty("paths");

    paths.TryGetProperty("/api/execution-logs/retention", out var retentionPath).Should().BeTrue();
    retentionPath.TryGetProperty("get", out _).Should().BeTrue();
    retentionPath.TryGetProperty("put", out var putOp).Should().BeTrue();

    putOp.TryGetProperty("requestBody", out var requestBody).Should().BeTrue();
    requestBody.TryGetProperty("content", out var content).Should().BeTrue();
    content.TryGetProperty("application/json", out _).Should().BeTrue();
  }

  [Fact]
  public async Task SwaggerDocumentIncludesExecutionLogListAndDetailEndpoints() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true);
    resp.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
    var paths = doc.RootElement.GetProperty("paths");

    paths.TryGetProperty("/api/execution-logs", out var listPath).Should().BeTrue();
    listPath.TryGetProperty("get", out _).Should().BeTrue();

    paths.TryGetProperty("/api/execution-logs/{id}", out var detailPath).Should().BeTrue();
    detailPath.TryGetProperty("get", out _).Should().BeTrue();
  }
}
