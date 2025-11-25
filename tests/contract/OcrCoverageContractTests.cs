using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class OcrCoverageContractTests : IDisposable {
  private static readonly JsonSerializerOptions SerializerOptions = new() {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    WriteIndented = true
  };
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevDataDir;
  private readonly string _coverageDir;

  public OcrCoverageContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");

    var dataRoot = Path.Combine(AppContext.BaseDirectory, "data");
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", dataRoot);
    _coverageDir = Path.Combine(dataRoot, "coverage");
    Directory.CreateDirectory(_coverageDir);
    TryDeleteSummary();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    TryDeleteSummary();
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task CoverageEndpointMatchesContract() {
    var generatedAt = DateTime.UtcNow;
    var sample = new CoverageSummaryPayload {
      GeneratedAtUtc = generatedAt,
      Namespace = "GameBot.Domain.Triggers.Evaluators",
      LineCoveragePercent = 72.4,
      TargetPercent = 70,
      Passed = true,
      UncoveredScenarios = new[] { "timeout-path" },
      ReportUrl = "https://example.com/reports/ocr/latest"
    };
    var summaryPath = Path.Combine(_coverageDir, "latest.json");
    var json = JsonSerializer.Serialize(sample, SerializerOptions);
    await File.WriteAllTextAsync(summaryPath, json).ConfigureAwait(true);

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var resp = await client.GetAsync(new Uri("/api/ocr/coverage", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var payload = await resp.Content.ReadFromJsonAsync<CoverageSummaryPayload>().ConfigureAwait(true);
    payload.Should().NotBeNull();
    payload!.GeneratedAtUtc.Should().BeCloseTo(generatedAt, TimeSpan.FromSeconds(1));
    payload.Namespace.Should().Be(sample.Namespace);
    payload.LineCoveragePercent.Should().Be(sample.LineCoveragePercent);
    payload.TargetPercent.Should().Be(sample.TargetPercent);
    payload.Passed.Should().BeTrue();
    payload.UncoveredScenarios.Should().BeEquivalentTo(sample.UncoveredScenarios);
    payload.ReportUrl.Should().Be(sample.ReportUrl);
  }

  private void TryDeleteSummary() {
    try {
      var summaryPath = Path.Combine(_coverageDir, "latest.json");
      if (File.Exists(summaryPath)) {
        File.Delete(summaryPath);
      }
    }
    catch { /* ignore */ }
  }

  private sealed class CoverageSummaryPayload {
    [JsonPropertyName("generatedAtUtc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("lineCoveragePercent")]
    public double LineCoveragePercent { get; set; }

    [JsonPropertyName("targetPercent")]
    public double TargetPercent { get; set; }

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("uncoveredScenarios")]
    public string[]? UncoveredScenarios { get; set; }

    [JsonPropertyName("reportUrl")]
    public string? ReportUrl { get; set; }
  }
}
