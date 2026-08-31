using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 079 (FR-019/FR-020/FR-021): with several runs in flight, each run's state stays its own and
/// each run's execution-log entries stay attributable to it.
///
/// The per-run state was already lock-guarded before this feature (the monitor has always read it on a
/// different thread than the run loop writes it); these are regression tests that exercise that under
/// genuine multi-run concurrency, which nothing did before.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class ConcurrentRunStateIsolationTests {
  public ConcurrentRunStateIsolationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateRunnableQueueAsync(HttpClient client, string name, string serial, bool cycle = true) {
    var created = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name, emulatorSerial = serial, cycleExecution = cycle }).ConfigureAwait(true);
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;

    var tplResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Tpl-" + Guid.NewGuid().ToString("N"), entries = new[] { new { sequenceId = $"seq-{name}" } }, overwrite = false }).ConfigureAwait(true);
    var tpl = JsonDocument.Parse(await tplResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
    await client.PutAsJsonAsync(new Uri($"/api/queues/{id}/template", UriKind.Relative), new { templateId = tpl }).ConfigureAwait(true);
    return id;
  }

  private static async Task<JsonElement> MonitorAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}/monitor", UriKind.Relative)).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  private static async Task WaitForRunningAsync(HttpClient client, string id, int timeoutMs = 8000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if ((await MonitorAsync(client, id).ConfigureAwait(true)).GetProperty("running").GetBoolean()) return;
      await Task.Delay(25).ConfigureAwait(true);
    }
  }

  [Fact] // FR-019/FR-020: neither run's monitor state ever shows the other's
  public async Task ConcurrentRunsNeverReportEachOthersState() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var a = await CreateRunnableQueueAsync(client, "Alpha", "emu-5558").ConfigureAwait(true);
    var b = await CreateRunnableQueueAsync(client, "Beta", "emu-5560").ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{a}/start", UriKind.Relative), null).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{b}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForRunningAsync(client, a).ConfigureAwait(true);
    await WaitForRunningAsync(client, b).ConfigureAwait(true);

    // Poll both monitors repeatedly and concurrently while the runs cycle, so a torn or shared read
    // would surface as a mismatched identity.
    for (var i = 0; i < 15; i++) {
      var snapshots = await Task.WhenAll(MonitorAsync(client, a), MonitorAsync(client, b)).ConfigureAwait(true);

      snapshots[0].GetProperty("queueId").GetString().Should().Be(a);
      snapshots[0].GetProperty("name").GetString().Should().Be("Alpha");
      snapshots[0].GetProperty("deviceSerial").GetString().Should().Be("emu-5558");

      snapshots[1].GetProperty("queueId").GetString().Should().Be(b);
      snapshots[1].GetProperty("name").GetString().Should().Be("Beta");
      snapshots[1].GetProperty("deviceSerial").GetString().Should().Be("emu-5560");
    }

    await client.PostAsync(new Uri($"/api/queues/{a}/stop", UriKind.Relative), null).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{b}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact] // FR-021: interleaved writes stay attributable — every entry belongs to exactly one run
  public async Task ExecutionLogEntriesFromConcurrentRunsStayAttributable() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var a = await CreateRunnableQueueAsync(client, "Alpha", "emu-5558", cycle: false).ConfigureAwait(true);
    var b = await CreateRunnableQueueAsync(client, "Beta", "emu-5560", cycle: false).ConfigureAwait(true);

    // Start together so the two runs' log writes interleave.
    await Task.WhenAll(
      client.PostAsync(new Uri($"/api/queues/{a}/start", UriKind.Relative), null),
      client.PostAsync(new Uri($"/api/queues/{b}/start", UriKind.Relative), null)).ConfigureAwait(true);

    var entriesA = await WaitForQueueEntriesAsync(client, a).ConfigureAwait(true);
    var entriesB = await WaitForQueueEntriesAsync(client, b).ConfigureAwait(true);

    entriesA.Should().NotBeEmpty("run A must have written its own entries");
    entriesB.Should().NotBeEmpty("run B must have written its own entries");
    entriesA.Should().OnlyContain(id => id == a);
    entriesB.Should().OnlyContain(id => id == b);
  }

  /// <summary>
  /// Polls the execution log until the queue has a finalized entry, returning the objectId of every
  /// entry the query attributes to it.
  /// </summary>
  private static async Task<List<string>> WaitForQueueEntriesAsync(HttpClient client, string queueId, int timeoutMs = 10000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      var resp = await client.GetAsync(new Uri($"/api/execution-logs?objectType=queue&objectId={queueId}&pageSize=20", UriKind.Relative)).ConfigureAwait(true);
      if (resp.IsSuccessStatusCode) {
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
        if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0) {
          var ids = items.EnumerateArray()
            .Select(e => e.GetProperty("objectRef").GetProperty("objectId").GetString() ?? string.Empty)
            .ToList();
          // Wait for the terminating entry so both runs have finished writing.
          if (items.EnumerateArray().Any(e => e.TryGetProperty("finalStatus", out var s)
                && !string.IsNullOrEmpty(s.GetString())
                && !string.Equals(s.GetString(), "running", StringComparison.OrdinalIgnoreCase))) {
            return ids;
          }
        }
      }
      await Task.Delay(50).ConfigureAwait(true);
    }
    return new List<string>();
  }
}
