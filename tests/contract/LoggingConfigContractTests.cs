using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class LoggingConfigContractTests : IDisposable
{
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string _dataRoot;

  public LoggingConfigContractTests()
  {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");

    _dataRoot = Path.Combine(AppContext.BaseDirectory, "data");
    Directory.CreateDirectory(_dataRoot);
    ResetConfigDirectory();
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    ResetConfigDirectory();

    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task PutComponentReturnsUpdatedComponent()
  {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateAuthedClient(app);

    var resp = await client.PutAsJsonAsync(
      "/config/logging/components/GameBot.Service",
      new LoggingComponentPatchPayload { Level = "Debug", Notes = "contract-test" })
      .ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var payload = await resp.Content.ReadFromJsonAsync<LoggingComponentResponse>().ConfigureAwait(true);
    payload.Should().NotBeNull();
    payload!.Name.Should().Be("GameBot.Service");
    payload.Enabled.Should().BeTrue();
    payload.EffectiveLevel.Should().Be("Debug");
    payload.Source.Should().Be("api");
  }

  [Fact]
  public async Task GetLoggingPolicyReturnsSnapshot()
  {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateAuthedClient(app);

    var resp = await client.GetAsync(new Uri("/config/logging", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var snapshot = await resp.Content.ReadFromJsonAsync<LoggingPolicySnapshotResponse>().ConfigureAwait(true);
    snapshot.Should().NotBeNull();
    snapshot!.Components.Should().NotBeNull();
    snapshot.Components!.Should().NotBeEmpty();
    snapshot.Components!.Any(c => c.Name == "GameBot.Service").Should().BeTrue();
  }

  private static HttpClient CreateAuthedClient(WebApplicationFactory<Program> app)
  {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private void ResetConfigDirectory()
  {
    try
    {
      var configDir = Path.Combine(_dataRoot, "config");
      if (Directory.Exists(configDir))
      {
        Directory.Delete(configDir, recursive: true);
      }
      Directory.CreateDirectory(configDir);
    }
    catch
    {
      // ignore cleanup errors
    }
  }

  private sealed class LoggingComponentPatchPayload
  {
    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
  }

  private sealed class LoggingComponentResponse
  {
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("effectiveLevel")]
    public string EffectiveLevel { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
  }

  private sealed class LoggingPolicySnapshotResponse
  {
    [JsonPropertyName("components")]
    public LoggingComponentResponse[]? Components { get; set; }
  }
}