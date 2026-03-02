using System.Diagnostics;
using FluentAssertions;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionLogsPerformanceIntegrationTests : IDisposable {
  private readonly ITestOutputHelper _output;
  private readonly string? _previousAuthToken;
  private readonly string? _previousDataDir;
  private readonly string? _previousPerfProfile;

  public ExecutionLogsPerformanceIntegrationTests(ITestOutputHelper output) {
    _output = output;
    _previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _previousDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _previousPerfProfile = Environment.GetEnvironmentVariable("GAMEBOT_PERF_PROFILE");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _previousAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _previousDataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_PERF_PROFILE", _previousPerfProfile);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task ListEndpointsMeetP95BudgetWithOneThousandLogs() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var executionLogService = app.Services.GetRequiredService<IExecutionLogService>();

    for (var i = 0; i < 1000; i++) {
      var status = i % 3 == 0 ? "failure" : "success";
      await executionLogService.LogCommandExecutionAsync(
        $"perf-cmd-{i:D4}",
        $"route-{i:D4}",
        status,
        Array.Empty<PrimitiveTapStepOutcome>(),
        new ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);
    }

    var localProfile = !string.Equals(
      Environment.GetEnvironmentVariable("GAMEBOT_PERF_PROFILE"),
      "ci",
      StringComparison.OrdinalIgnoreCase);

    var firstOpenBudgetMs = localProfile ? 100d : 200d;
    var filterSortBudgetMs = localProfile ? 300d : 450d;

    var firstOpenDurations = await MeasureQueryDurationsAsync(
      client,
      "/api/execution-logs?pageSize=50",
      25).ConfigureAwait(false);

    var filterSortDurations = await MeasureQueryDurationsAsync(
      client,
      "/api/execution-logs?sortBy=objectName&sortDirection=asc&filterStatus=success&filterObjectName=route-0&pageSize=50",
      25).ConfigureAwait(false);

    var firstOpenP95 = CalculateP95(firstOpenDurations);
    var filterSortP95 = CalculateP95(filterSortDurations);

    _output.WriteLine($"Execution logs first-open p95: {firstOpenP95:F2} ms (budget < {firstOpenBudgetMs:F0} ms)");
    _output.WriteLine($"Execution logs filter/sort p95: {filterSortP95:F2} ms (budget < {filterSortBudgetMs:F0} ms)");

    firstOpenP95.Should().BeLessThan(firstOpenBudgetMs, "first-open list should remain within p95 budget");
    filterSortP95.Should().BeLessThan(filterSortBudgetMs, "filter/sort update should remain within p95 budget");
  }

  private static async Task<List<double>> MeasureQueryDurationsAsync(HttpClient client, string relativeUri, int iterations) {
    var durations = new List<double>(iterations);

    var warmup = await client.GetAsync(new Uri(relativeUri, UriKind.Relative)).ConfigureAwait(false);
    warmup.EnsureSuccessStatusCode();

    for (var i = 0; i < iterations; i++) {
      var stopwatch = Stopwatch.StartNew();
      var response = await client.GetAsync(new Uri(relativeUri, UriKind.Relative)).ConfigureAwait(false);
      stopwatch.Stop();
      response.EnsureSuccessStatusCode();
      durations.Add(stopwatch.Elapsed.TotalMilliseconds);
    }

    return durations;
  }

  private static double CalculateP95(List<double> durationsMs) {
    durationsMs.Sort();
    var p95Index = (int)Math.Ceiling(durationsMs.Count * 0.95d) - 1;
    return durationsMs[Math.Max(0, p95Index)];
  }
}
