using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Exercises GET /api/queues/{id}/monitor through the real HTTP stack. ADB runs in stub mode, so a
/// cycling queue holds a deterministic "Running" window while the snapshot is read (feature 072).
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueMonitorEndpointTests {
  public QueueMonitorEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateQueueAsync(HttpClient client, bool cycle) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Mon", emulatorSerial = "emu-offline", cycleExecution = cycle }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  // Template with a OncePerRun spine step and a time-of-day Timer entry (so the snapshot carries an
  // expectedAt that must serialize with a numeric offset, FR-014).
  private static async Task<string> CreateTemplateAsync(HttpClient client) {
    var entries = new object[] {
      new { sequenceId = "seq-loop", scheduleType = "OncePerRun", timerTimeOfDay = (string?)null },
      new { sequenceId = "seq-timer", scheduleType = "Timer", timerTimeOfDay = "23:59" }
    };
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Tpl-" + Guid.NewGuid().ToString("N"), entries, overwrite = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> StatusAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("status").GetString()!;
  }

  private static async Task WaitForStatusAsync(HttpClient client, string id, string expected, int timeoutMs = 5000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if (await StatusAsync(client, id).ConfigureAwait(true) == expected) return;
      await Task.Delay(25).ConfigureAwait(true);
    }
  }

  [Fact]
  public async Task MonitorForUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var resp = await client.GetAsync(new Uri("/api/queues/missing/monitor", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task MonitorForStoppedQueueReturns200NotRunning() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client, cycle: false).ConfigureAwait(true);

    var resp = await client.GetAsync(new Uri($"/api/queues/{id}/monitor", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    root.GetProperty("running").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task MonitorForRunningQueueReturnsPopulatedSnapshotWithOffsetTimes() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    var tpl = await CreateTemplateAsync(client).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{id}/template", UriKind.Relative), new { templateId = tpl }).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, id, "Running").ConfigureAwait(true);

    var resp = await client.GetAsync(new Uri($"/api/queues/{id}/monitor", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;

    root.GetProperty("running").GetBoolean().Should().BeTrue();

    // The timer entry must appear in upcoming with an expectedAt serialized as ISO-8601 WITH a numeric
    // offset (…+hh:mm / …-hh:mm), never a bare/UTC-Z instant (FR-014).
    var upcoming = root.GetProperty("upcoming");
    upcoming.GetArrayLength().Should().BeGreaterThan(0);
    string? expectedAt = null;
    foreach (var item in upcoming.EnumerateArray()) {
      if (item.GetProperty("scheduleKind").GetString() == "TimerTimeOfDay") {
        expectedAt = item.GetProperty("expectedAt").GetString();
      }
    }
    expectedAt.Should().NotBeNull();
    expectedAt.Should().NotEndWith("Z");
    Regex.IsMatch(expectedAt!, @"[+-]\d{2}:\d{2}$").Should().BeTrue($"expectedAt '{expectedAt}' should carry a numeric offset");

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }
}
