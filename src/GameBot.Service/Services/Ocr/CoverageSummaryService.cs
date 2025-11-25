using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.Ocr;

internal interface ICoverageSummaryService {
  Task<CoverageSummaryResult> GetLatestAsync(CancellationToken cancellationToken = default);
}

internal sealed class CoverageSummaryService : ICoverageSummaryService {
  private readonly string _summaryPath;
  private readonly TimeSpan _staleAfter;
  private readonly ILogger<CoverageSummaryService> _logger;
  private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };
  private readonly Func<DateTime> _utcNow;
  private static readonly Action<ILogger, string, Exception?> InvalidSummaryLog = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(52010, nameof(InvalidSummaryLog)), "Coverage summary at {SummaryPath} is missing required fields");
  private static readonly Action<ILogger, string, TimeSpan, Exception?> StaleSummaryLog = LoggerMessage.Define<string, TimeSpan>(LogLevel.Information, new EventId(52011, nameof(StaleSummaryLog)), "Coverage summary at {SummaryPath} is stale (age={Age})");
  private static readonly Action<ILogger, string, Exception?> ParseFailureLog = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(52012, nameof(ParseFailureLog)), "Failed to parse coverage summary at {SummaryPath}");
  private static readonly Action<ILogger, string, Exception?> ReadFailureLog = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(52013, nameof(ReadFailureLog)), "Failed to read coverage summary at {SummaryPath}");

  public CoverageSummaryService(string storageRoot, ILogger<CoverageSummaryService> logger, TimeSpan? staleAfter = null, Func<DateTime>? utcNow = null) {
    _summaryPath = Path.Combine(storageRoot, "coverage", "latest.json");
    _staleAfter = staleAfter ?? TimeSpan.FromHours(24);
    _logger = logger;
    _utcNow = utcNow ?? (() => DateTime.UtcNow);
  }

  public async Task<CoverageSummaryResult> GetLatestAsync(CancellationToken cancellationToken = default) {
    if (!File.Exists(_summaryPath)) {
      return CoverageSummaryResult.Missing();
    }

    try {
      using var stream = File.OpenRead(_summaryPath);
      var payload = await JsonSerializer.DeserializeAsync<CoverageSummaryPayload>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
      if (payload is null || payload.GeneratedAtUtc == default || string.IsNullOrWhiteSpace(payload.Namespace)) {
        InvalidSummaryLog(_logger, _summaryPath, null);
        return CoverageSummaryResult.Invalid();
      }

      var summary = new OcrCoverageSummary {
        GeneratedAtUtc = payload.GeneratedAtUtc,
        Namespace = payload.Namespace,
        LineCoveragePercent = payload.LineCoveragePercent,
        TargetPercent = payload.TargetPercent,
        Passed = payload.Passed,
        UncoveredScenarios = payload.UncoveredScenarios?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? Array.Empty<string>(),
        ReportUrl = payload.ReportUrl
      };

      var age = _utcNow().Subtract(summary.GeneratedAtUtc);
      if (age > _staleAfter) {
        StaleSummaryLog(_logger, _summaryPath, age, null);
        return CoverageSummaryResult.Stale(summary);
      }

      return CoverageSummaryResult.Success(summary);
    }
    catch (JsonException ex) {
      ParseFailureLog(_logger, _summaryPath, ex);
      return CoverageSummaryResult.Invalid();
    }
    catch (IOException ex) {
      ReadFailureLog(_logger, _summaryPath, ex);
      return CoverageSummaryResult.Invalid();
    }
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

internal sealed record CoverageSummaryResult(CoverageSummaryStatus Status, OcrCoverageSummary? Summary) {
  public static CoverageSummaryResult Missing() => new(CoverageSummaryStatus.Missing, null);
  public static CoverageSummaryResult Invalid() => new(CoverageSummaryStatus.Invalid, null);
  public static CoverageSummaryResult Stale(OcrCoverageSummary summary) => new(CoverageSummaryStatus.Stale, summary);
  public static CoverageSummaryResult Success(OcrCoverageSummary summary) => new(CoverageSummaryStatus.Success, summary);
}

internal enum CoverageSummaryStatus {
  Success,
  Missing,
  Stale,
  Invalid
}

internal sealed class OcrCoverageSummary {
  public DateTime GeneratedAtUtc { get; init; }
  public string Namespace { get; init; } = string.Empty;
  public double LineCoveragePercent { get; init; }
  public double TargetPercent { get; init; }
  public bool Passed { get; init; }
  public IReadOnlyList<string> UncoveredScenarios { get; init; } = Array.Empty<string>();
  public string? ReportUrl { get; init; }
}
