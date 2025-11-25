using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class CoverageEndpointTests {
  private static readonly JsonSerializerOptions SerializerOptions = new() {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    WriteIndented = true
  };

  public CoverageEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task CoverageEndpointReturnsLatestSummary() {
    var dataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")!;
    var coverageDir = Path.Combine(dataDir, "coverage");
    Directory.CreateDirectory(coverageDir);

    var generatedAt = DateTime.UtcNow;
    var expected = new CoverageSummaryPayload {
      GeneratedAtUtc = generatedAt,
      Namespace = "GameBot.Domain.Triggers.Evaluators",
      LineCoveragePercent = 74.2,
      TargetPercent = 70,
      Passed = true,
      UncoveredScenarios = new[] { "missing-negative-test" },
      ReportUrl = "https://example.com/report"
    };
    var summaryPath = Path.Combine(coverageDir, "latest.json");
    await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(expected, SerializerOptions)).ConfigureAwait(true);

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var resp = await client.GetAsync(new Uri("/api/ocr/coverage", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var payload = await resp.Content.ReadFromJsonAsync<CoverageSummaryPayload>().ConfigureAwait(true);
    payload.Should().NotBeNull();
    payload!.GeneratedAtUtc.Should().BeCloseTo(generatedAt, TimeSpan.FromSeconds(1));
    payload.Namespace.Should().Be(expected.Namespace);
    payload.LineCoveragePercent.Should().Be(expected.LineCoveragePercent);
    payload.TargetPercent.Should().Be(expected.TargetPercent);
    payload.Passed.Should().BeTrue();
    payload.UncoveredScenarios.Should().BeEquivalentTo(expected.UncoveredScenarios);
    payload.ReportUrl.Should().Be(expected.ReportUrl);
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
