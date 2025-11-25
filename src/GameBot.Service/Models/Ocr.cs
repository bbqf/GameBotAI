using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameBot.Service.Models;

internal sealed class OcrCoverageSummaryResponse {
  [JsonPropertyName("generatedAtUtc")]
  public DateTime GeneratedAtUtc { get; init; }

  [JsonPropertyName("namespace")]
  public string Namespace { get; init; } = string.Empty;

  [JsonPropertyName("lineCoveragePercent")]
  public double LineCoveragePercent { get; init; }

  [JsonPropertyName("targetPercent")]
  public double TargetPercent { get; init; }

  [JsonPropertyName("passed")]
  public bool Passed { get; init; }

  [JsonPropertyName("uncoveredScenarios")]
  public IReadOnlyList<string>? UncoveredScenarios { get; init; }

  [JsonPropertyName("reportUrl")]
  public string? ReportUrl { get; init; }
}
