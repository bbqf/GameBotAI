using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Exercises the queue observability surface against a real run (feature 086, issue #180): the health
/// block on GET /api/queues/{id} and the per-cycle records on GET /api/queues/{id}/cycles, both read
/// <b>while the queue is still running</b>.
/// <para>
/// ADB runs in stub mode, so a cycling queue holds a deterministic Running window to read against.
/// </para>
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueCyclesEndpointTests {
  public QueueCyclesEndpointTests() {
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
      new { name = "Cyc", emulatorSerial = "emu-offline", cycleExecution = cycle }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateSequenceAsync(HttpClient client, string name) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative),
      new { name, steps = Array.Empty<string>() }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateTemplateAsync(HttpClient client, string sequenceId) {
    var entries = new object[] {
      new { sequenceId, scheduleType = "OncePerRun", timerTimeOfDay = (string?)null }
    };
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Tpl-" + Guid.NewGuid().ToString("N"), entries, overwrite = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path) {
    var resp = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  private static async Task<string> StatusAsync(HttpClient client, string id) =>
    (await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true)).GetProperty("status").GetString()!;

  private static async Task WaitForStatusAsync(HttpClient client, string id, string expected, int timeoutMs = 5000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if (await StatusAsync(client, id).ConfigureAwait(true) == expected) return;
      await Task.Delay(25).ConfigureAwait(true);
    }
  }

  /// <summary>Polls the health block until at least <paramref name="target"/> cycles have completed.</summary>
  private static async Task<JsonElement> WaitForCyclesAsync(HttpClient client, string id, int target, int timeoutMs = 10000) {
    var sw = Stopwatch.StartNew();
    JsonElement health = default;
    while (sw.ElapsedMilliseconds < timeoutMs) {
      var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);
      if (root.GetProperty("health").ValueKind == JsonValueKind.Object) {
        health = root.GetProperty("health").Clone();
        if (health.GetProperty("cyclesCompleted").GetInt32() >= target) return health;
      }
      await Task.Delay(25).ConfigureAwait(true);
    }
    return health;
  }

  private static async Task<(string queueId, string sequenceId)> StartCyclingQueueAsync(HttpClient client) {
    var seq = await CreateSequenceAsync(client, "Cycle Step " + Guid.NewGuid().ToString("N")[..6]).ConfigureAwait(true);
    var id = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    var tpl = await CreateTemplateAsync(client, seq).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{id}/template", UriKind.Relative), new { templateId = tpl }).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, id, "Running").ConfigureAwait(true);
    return (id, seq);
  }

  [Fact]
  public async Task RunningQueueExposesAdvancingCycleHealth() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var (id, _) = await StartCyclingQueueAsync(client).ConfigureAwait(true);

    var health = await WaitForCyclesAsync(client, id, target: 2).ConfigureAwait(true);

    // The signal the platform did not have: the queue is Running AND demonstrably doing work.
    (await StatusAsync(client, id).ConfigureAwait(true)).Should().Be("Running");
    health.ValueKind.Should().Be(JsonValueKind.Object, "a running queue carries a health block");
    health.GetProperty("cyclesCompleted").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    health.GetProperty("runStartedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    health.GetProperty("lastCycleStartedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    health.GetProperty("lastCycleCompletedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    health.GetProperty("lastCycleStatus").GetString().Should().BeOneOf("success", "failure");

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact]
  public async Task RunningQueueExposesPerCycleRecordsWithoutStoppingIt() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var (id, seqId) = await StartCyclingQueueAsync(client).ConfigureAwait(true);
    await WaitForCyclesAsync(client, id, target: 2).ConfigureAwait(true);

    var root = await GetJsonAsync(client, $"/api/queues/{id}/cycles?limit=5").ConfigureAwait(true);

    // Still running — reading the cycles did not require stopping the queue.
    (await StatusAsync(client, id).ConfigureAwait(true)).Should().Be("Running");
    root.GetProperty("running").GetBoolean().Should().BeTrue();

    var cycles = root.GetProperty("cycles");
    cycles.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

    var newest = cycles[0];
    var older = cycles[1];
    newest.GetProperty("ordinal").GetInt32().Should()
      .BeGreaterThan(older.GetProperty("ordinal").GetInt32(), "cycles are returned newest first");
    newest.GetProperty("status").GetString().Should().BeOneOf("success", "failure");
    newest.GetProperty("startedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    newest.GetProperty("completedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

    var entries = newest.GetProperty("entries");
    entries.GetArrayLength().Should().BeGreaterThan(0, "the roster entry executed in this cycle is recorded");
    entries[0].GetProperty("sequenceId").GetString().Should().Be(seqId);
    entries[0].GetProperty("sequenceName").ValueKind.Should()
      .NotBe(JsonValueKind.Null, "the name is resolved when the response is built");
    entries[0].GetProperty("status").GetString().Should().BeOneOf("success", "failure");

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact]
  public async Task CycleLimitIsHonouredAndClamped() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var (id, _) = await StartCyclingQueueAsync(client).ConfigureAwait(true);
    await WaitForCyclesAsync(client, id, target: 3).ConfigureAwait(true);

    var limited = await GetJsonAsync(client, $"/api/queues/{id}/cycles?limit=1").ConfigureAwait(true);
    limited.GetProperty("cycles").GetArrayLength().Should().Be(1);

    var clampedHigh = await GetJsonAsync(client, $"/api/queues/{id}/cycles?limit=9999").ConfigureAwait(true);
    clampedHigh.GetProperty("cycles").GetArrayLength().Should().BeLessThanOrEqualTo(50);

    var clampedLow = await GetJsonAsync(client, $"/api/queues/{id}/cycles?limit=0").ConfigureAwait(true);
    clampedLow.GetProperty("cycles").GetArrayLength().Should()
      .Be(1, "a zero limit clamps to one rather than failing");

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact]
  public async Task HealthIsClearedWhenTheRunEnds() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var (id, _) = await StartCyclingQueueAsync(client).ConfigureAwait(true);
    await WaitForCyclesAsync(client, id, target: 1).ConfigureAwait(true);

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, id, "Stopped").ConfigureAwait(true);

    var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);
    root.GetProperty("health").ValueKind.Should()
      .Be(JsonValueKind.Null, "health describes the current run, and there is none");

    var cycles = await GetJsonAsync(client, $"/api/queues/{id}/cycles").ConfigureAwait(true);
    cycles.GetProperty("running").GetBoolean().Should().BeFalse();
    cycles.GetProperty("cycles").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task TwoConcurrentQueuesReportTheirOwnCyclesIndependently() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var seqA = await CreateSequenceAsync(client, "Seq A " + Guid.NewGuid().ToString("N")[..6]).ConfigureAwait(true);
    var seqB = await CreateSequenceAsync(client, "Seq B " + Guid.NewGuid().ToString("N")[..6]).ConfigureAwait(true);

    var idA = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    var tplA = await CreateTemplateAsync(client, seqA).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{idA}/template", UriKind.Relative), new { templateId = tplA }).ConfigureAwait(true);

    // A second queue on a DIFFERENT device, so the run's exclusive device claim does not refuse it.
    var respB = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "CycB", emulatorSerial = "emu-offline-2", cycleExecution = true }).ConfigureAwait(true);
    var idB = JsonDocument.Parse(await respB.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
    var tplB = await CreateTemplateAsync(client, seqB).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{idB}/template", UriKind.Relative), new { templateId = tplB }).ConfigureAwait(true);

    await client.PostAsync(new Uri($"/api/queues/{idA}/start", UriKind.Relative), null).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{idB}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, idA, "Running").ConfigureAwait(true);
    await WaitForStatusAsync(client, idB, "Running").ConfigureAwait(true);
    await WaitForCyclesAsync(client, idA, target: 1).ConfigureAwait(true);
    await WaitForCyclesAsync(client, idB, target: 1).ConfigureAwait(true);

    var cyclesA = await GetJsonAsync(client, $"/api/queues/{idA}/cycles").ConfigureAwait(true);
    var cyclesB = await GetJsonAsync(client, $"/api/queues/{idB}/cycles").ConfigureAwait(true);

    cyclesA.GetProperty("queueId").GetString().Should().Be(idA);
    cyclesB.GetProperty("queueId").GetString().Should().Be(idB);
    cyclesA.GetProperty("cycles")[0].GetProperty("entries")[0].GetProperty("sequenceId").GetString().Should()
      .Be(seqA, "each run records only its own work");
    cyclesB.GetProperty("cycles")[0].GetProperty("entries")[0].GetProperty("sequenceId").GetString().Should()
      .Be(seqB);

    await client.PostAsync(new Uri($"/api/queues/{idA}/stop", UriKind.Relative), null).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{idB}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  /// <summary>
  /// FR-018: this feature writes nothing to the execution log. A completed run still produces its
  /// usual terminating record, with its existing status and summary wording.
  /// </summary>
  [Fact]
  public async Task TerminatingRunRecordIsUnchanged() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var seq = await CreateSequenceAsync(client, "Logged " + Guid.NewGuid().ToString("N")[..6]).ConfigureAwait(true);
    var id = await CreateQueueAsync(client, cycle: false).ConfigureAwait(true);
    var tpl = await CreateTemplateAsync(client, seq).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{id}/template", UriKind.Relative), new { templateId = tpl }).ConfigureAwait(true);

    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, id, "Stopped").ConfigureAwait(true);

    var svc = app.Services.GetRequiredService<IExecutionLogService>();
    var page = await svc.QueryAsync(new ExecutionLogQuery { ObjectType = "queue", RootsOnly = true, PageSize = 100 }).ConfigureAwait(true);
    var runRecord = page.Items.FirstOrDefault(e => e.ObjectRef.ObjectId == id);

    runRecord.Should().NotBeNull("a run still writes exactly one terminating record");
    runRecord!.FinalStatus.Should().Be("success");
    runRecord.Summary.Should().Contain("sequence(s) executed",
      "the existing summary wording is untouched by cycle observability");
  }

  [Fact]
  public async Task NonCyclingQueueCompletesAndLeavesNoLiveState() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var seq = await CreateSequenceAsync(client, "Single " + Guid.NewGuid().ToString("N")[..6]).ConfigureAwait(true);
    var id = await CreateQueueAsync(client, cycle: false).ConfigureAwait(true);
    var tpl = await CreateTemplateAsync(client, seq).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{id}/template", UriKind.Relative), new { templateId = tpl }).ConfigureAwait(true);

    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, id, "Stopped").ConfigureAwait(true);

    // A single-pass run ends on its own; the ledger dies with it, so the queue reads as it always did.
    var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);
    root.GetProperty("status").GetString().Should().Be("Stopped");
    root.GetProperty("health").ValueKind.Should().Be(JsonValueKind.Null);

    // FR-019's "exactly one cycle" is asserted at the ledger level in QueueCycleLedgerTests, where the
    // non-cycling loop shape can be driven deterministically instead of raced against a run that ends
    // in milliseconds.
  }
}
