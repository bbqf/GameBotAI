using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace GameBot.IntegrationTests;

public class SwaggerPerfTests {
  private readonly ITestOutputHelper _output;

  public SwaggerPerfTests(ITestOutputHelper output) {
    _output = output;
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task SwaggerDocsp95below300ms() {
    using var app = new WebApplicationFactory<Program>();
    using var client = app.CreateClient();
    var endpoint = new Uri("/swagger/v1/swagger.json", UriKind.Relative);

    // Warm-up to avoid first-hit overhead
    var warmup = await client.GetAsync(endpoint).ConfigureAwait(true);
    warmup.EnsureSuccessStatusCode();

    var durations = new List<double>();
    const int iterations = 20;

    for (var i = 0; i < iterations; i++) {
      var sw = Stopwatch.StartNew();
      var resp = await client.GetAsync(endpoint).ConfigureAwait(true);
      sw.Stop();
      resp.EnsureSuccessStatusCode();
      durations.Add(sw.Elapsed.TotalMilliseconds);
    }

    durations.Sort();
    var p95Index = (int)Math.Ceiling(0.95 * durations.Count) - 1;
    var p95 = durations[Math.Max(0, p95Index)];

    // Shared CI runners (and coverlet instrumentation in the coverage gates) inflate latencies,
    // so allow extra headroom there while keeping the strict local budget.
    var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
    var budgetMs = isCi ? 600.0 : 300.0;
    _output.WriteLine($"Swagger docs p95: {p95:F1} ms (n={durations.Count}, budget < {budgetMs:F0} ms)");

    p95.Should().BeLessThan(budgetMs, "Swagger docs endpoint should stay within its p95 budget");
  }
}
