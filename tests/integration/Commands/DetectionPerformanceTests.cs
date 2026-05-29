using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

[Collection("ConfigIsolation")]
public sealed class DetectionPerformanceTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public DetectionPerformanceTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task DetectionSaveReloadP95Under500ms() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var durations = new List<long>();
    for (var i = 0; i < 6; i++) {
      var sw = Stopwatch.StartNew();
      var createResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new {
        name = $"perf-cmd-{i}",
        steps = new[] {
          new {
            type = "PrimitiveTap",
            order = 0,
            primitiveTap = new {
              detectionTarget = new {
                referenceImageId = "home_button",
                confidence = 0.8,
                offsetX = 0,
                offsetY = 0,
                selectionStrategy = "HighestConfidence"
              }
            }
          }
        },
        detection = new { referenceImageId = "home_button", confidence = 0.8, offsetX = 0, offsetY = 0 }
      }).ConfigureAwait(true);
      createResp.StatusCode.Should().Be(HttpStatusCode.Created);
      var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
      var commandId = created!["id"]!.ToString();

      var getResp = await client.GetAsync(new Uri($"/api/commands/{commandId}", UriKind.Relative)).ConfigureAwait(true);
      getResp.StatusCode.Should().Be(HttpStatusCode.OK);
      sw.Stop();
      durations.Add(sw.ElapsedMilliseconds);
    }

    var ordered = durations.OrderBy(x => x).ToArray();
    var p95Index = (int)Math.Ceiling(0.95 * ordered.Length) - 1;
    var p95 = ordered[Math.Max(p95Index, 0)];

    p95.Should().BeLessThan(500, "detection save/reload should remain snappy");
  }
}
