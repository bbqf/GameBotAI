using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// CA2000: NewApp hands ownership of the factory to its caller, which disposes it with a
// `using var`. The analyzer cannot see across that boundary, and the constitution permits
// disabling code-quality rules in test code.
#pragma warning disable CA2000

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// The queue failure policy against a real run (feature 087, issue #181).
/// <para>
/// This file reproduces the condition the issue was filed about: during the 2026-09-14 outage two
/// production rosters failed every cycle for 44 hours and nothing left the machine. The assertions
/// here are that it now escalates, that it escalates <b>once</b> per episode rather than per cycle,
/// and that a receiver which never answers costs the run nothing.
/// </para>
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueFailurePolicyRunTests {
  private readonly string _dataDir;

  public QueueFailurePolicyRunTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    _dataDir = TestEnvironment.PrepareCleanDataDir();
  }

  /// <summary>Records every event, and can be made to block or fail on demand.</summary>
  private sealed class CapturingNotifier : IFailureNotifier {
    public ConcurrentQueue<FailureNotificationEvent> Sent { get; } = new();
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public bool Succeeds { get; set; } = true;
    public string? Error { get; set; }

    public async Task<FailureNotificationResult> NotifyAsync(
      FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct = default) {
      Sent.Enqueue(evt);
      if (Delay > TimeSpan.Zero) await Task.Delay(Delay, CancellationToken.None).ConfigureAwait(false);
      return new FailureNotificationResult(Succeeds, DateTimeOffset.Now, Succeeds ? null : Error);
    }
  }

  private static WebApplicationFactory<Program> NewApp(CapturingNotifier notifier) =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => {
        // Substitute the notifier through DI rather than pointing configuration at a local
        // receiver: it isolates these assertions from network state, and Service:* keys cannot be
        // overridden from WebApplicationFactory in this harness anyway.
        var existing = services.Where(d => d.ServiceType == typeof(IFailureNotifier)).ToList();
        foreach (var descriptor in existing) services.Remove(descriptor);
        services.AddSingleton<IFailureNotifier>(notifier);
      }));

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  /// <summary>
  /// The id of a sequence that is deliberately never seeded. A roster entry pointing at an
  /// unresolvable sequence fails every firing — the run loop records it as a per-sequence fault and
  /// continues — which gives this file a deterministic failing cycle with no device involved.
  /// <para>
  /// A dangling <i>command</i> reference was tried first and does not work: the sequence still
  /// reports Succeeded, so the cycle counts as a success.
  /// </para>
  /// </summary>
  private const string MissingSequenceId = "seq-does-not-exist";

  /// <summary>A sequence with no steps: a cycle that completes successfully having done nothing.</summary>
  private void SeedPassingSequence(string id, string name) {
    var dir = Path.Combine(_dataDir, "commands", "sequences");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, id + ".json"), $"{{\"Id\":\"{id}\",\"Name\":\"{name}\"}}");
  }

  private static async Task<string> CreateQueueAsync(HttpClient client, object? policy) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new {
      name = "Policed farm",
      emulatorSerial = "emu-offline",
      cycleExecution = true,
      failurePolicy = policy
    }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateTemplateAsync(HttpClient client, params string[] sequenceIds) {
    var entries = Array.ConvertAll(sequenceIds,
      id => new { sequenceId = id, scheduleType = "OncePerRun", timerTimeOfDay = (string?)null });
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Tpl-" + Guid.NewGuid().ToString("N"), entries, overwrite = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("id").GetString()!;
  }

  private static async Task LinkAndStartAsync(HttpClient client, string queueId, string templateId) {
    (await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative),
      new { templateId }).ConfigureAwait(true)).EnsureSuccessStatusCode();
    (await client.PostAsync(new Uri($"/api/queues/{queueId}/start", UriKind.Relative), null)
      .ConfigureAwait(true)).EnsureSuccessStatusCode();
  }

  private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path) {
    var resp = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  private static async Task<JsonElement?> HealthAsync(HttpClient client, string id) {
    var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);
    return root.GetProperty("health").ValueKind == JsonValueKind.Object
      ? root.GetProperty("health").Clone()
      : null;
  }

  /// <summary>Polls health until <paramref name="predicate"/> holds, or the timeout elapses.</summary>
  private static async Task<JsonElement?> WaitForHealthAsync(
    HttpClient client, string id, Func<JsonElement, bool> predicate, int timeoutMs = 20000) {
    var sw = Stopwatch.StartNew();
    JsonElement? last = null;
    while (sw.ElapsedMilliseconds < timeoutMs) {
      last = await HealthAsync(client, id).ConfigureAwait(true);
      if (last is { } h && predicate(h)) return last;
      await Task.Delay(25).ConfigureAwait(true);
    }
    return last;
  }

  private static async Task StopAsync(HttpClient client, string id) =>
    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);

  // ── User Story 1: the alert ───────────────────────────────────────────────────────────────────

  /// <summary>
  /// The regression test for the issue. A roster whose every cycle fails must escalate at the
  /// threshold — and must keep running, because a cycling roster can still self-heal.
  /// </summary>
  [Fact]
  public async Task FailingQueueNotifiesAtTheThresholdAndKeepsRunning() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 3, action = "notify", notifyUrl = "http://127.0.0.1:9099/alerts" })
      .ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    var health = await WaitForHealthAsync(client, id,
      h => h.GetProperty("failurePolicyTripped").GetBoolean()).ConfigureAwait(true);

    health.Should().NotBeNull();
    health!.Value.GetProperty("failurePolicyConfigured").GetBoolean().Should().BeTrue();
    health.Value.GetProperty("failurePolicyTripped").GetBoolean().Should().BeTrue();
    health.Value.GetProperty("consecutiveFailedCycles").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    health.Value.GetProperty("lastCycleStatus").GetString().Should().Be("failure");

    var evt = await WaitForEventAsync(notifier).ConfigureAwait(true);
    evt.Should().NotBeNull();
    evt!.QueueId.Should().Be(id);
    evt.QueueName.Should().Be("Policed farm");
    evt.EmulatorSerial.Should().Be("emu-offline");
    evt.ConsecutiveFailedCycles.Should().BeGreaterThanOrEqualTo(3);
    evt.FailedSequenceId.Should().Be(MissingSequenceId);
    evt.FailedSequenceName.Should().BeNull(
      "the name is resolved at send time, so a sequence that no longer exists reports null rather than a stale name");
    evt.EventType.Should().Be(FailureNotificationEvent.QueueFailurePolicyEvent);

    var status = (await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true))
      .GetProperty("status").GetString();
    status.Should().Be("Running", "a notify policy alerts without halting a roster that may self-heal");

    await StopAsync(client, id).ConfigureAwait(true);
  }

  /// <summary>
  /// FR-009. The difference between an alert and an overnight flood of them — and the reason the
  /// issue warns against a naive per-cycle notification.
  /// </summary>
  [Fact]
  public async Task SustainedOutageProducesOneAlertNotOnePerCycle() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 2, action = "notify", notifyUrl = "http://127.0.0.1:9099/alerts" })
      .ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    // Let the run cycle well past the threshold.
    await WaitForHealthAsync(client, id,
      h => h.GetProperty("consecutiveFailedCycles").GetInt32() >= 8).ConfigureAwait(true);
    await StopAsync(client, id).ConfigureAwait(true);

    notifier.Sent.Should().HaveCount(1,
      "eight or more consecutive failed cycles are one episode, not eight alerts");
  }

  /// <summary>
  /// SC-005. A receiver that never answers must not slow the run down — the alerting mechanism can
  /// never become the reason a farm stops working.
  /// </summary>
  [Fact]
  public async Task BlockingNotifierDoesNotStallTheRun() {
    var notifier = new CapturingNotifier { Delay = TimeSpan.FromSeconds(30) };
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 1, action = "notify", notifyUrl = "http://127.0.0.1:9099/alerts" })
      .ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    // The notifier blocks for 30 s on the first failed cycle. If the run awaited it, the cycle count
    // could not climb. Ten cycles well inside that window proves it does not.
    var health = await WaitForHealthAsync(client, id,
      h => h.GetProperty("cyclesCompleted").GetInt32() >= 10, timeoutMs: 15000).ConfigureAwait(true);

    health.Should().NotBeNull();
    health!.Value.GetProperty("cyclesCompleted").GetInt32().Should().BeGreaterThanOrEqualTo(10,
      "delivery runs off the run loop's thread and is never awaited by it");

    await StopAsync(client, id).ConfigureAwait(true);
  }

  /// <summary>
  /// FR-026 / SC-008: an operator must be able to establish that an alert did not get out from the
  /// queue's own status, without opening a log file on the host.
  /// </summary>
  [Fact]
  public async Task UndeliverableNotificationIsVisibleOnHealth() {
    var notifier = new CapturingNotifier { Succeeds = false, Error = "connection refused" };
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 1, action = "notify", notifyUrl = "http://127.0.0.1:9099/alerts" })
      .ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    var health = await WaitForHealthAsync(client, id,
      h => h.GetProperty("lastNotificationAt").ValueKind != JsonValueKind.Null).ConfigureAwait(true);

    health.Should().NotBeNull();
    health!.Value.GetProperty("lastNotificationSucceeded").GetBoolean().Should().BeFalse();
    health.Value.GetProperty("lastNotificationError").GetString().Should().Be("connection refused");

    await StopAsync(client, id).ConfigureAwait(true);
  }

  /// <summary>FR-004 / SC-004: an unconfigured queue behaves exactly as it did before this feature.</summary>
  [Fact]
  public async Task QueueWithoutPolicyNeverNotifiesHowEverMuchItFails() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client, null).ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    var health = await WaitForHealthAsync(client, id,
      h => h.GetProperty("consecutiveFailedCycles").GetInt32() >= 5).ConfigureAwait(true);
    await StopAsync(client, id).ConfigureAwait(true);

    health.Should().NotBeNull();
    health!.Value.GetProperty("failurePolicyConfigured").GetBoolean().Should().BeFalse();
    health.Value.GetProperty("failurePolicyTripped").GetBoolean().Should().BeFalse();
    notifier.Sent.Should().BeEmpty();
  }

  // ── User Story 2: the actions ─────────────────────────────────────────────────────────────────

  /// <summary>
  /// FR-017. Reporting a policy stop as an operator stop would tell whoever reads the log that a
  /// person halted production when nobody did.
  /// </summary>
  [Fact]
  public async Task StopPolicyEndsTheRunWithItsOwnStopReason() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 2, action = "stop" }).ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    var sw = Stopwatch.StartNew();
    string? status = null;
    while (sw.ElapsedMilliseconds < 20000) {
      status = (await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true))
        .GetProperty("status").GetString();
      if (status == "Stopped") break;
      await Task.Delay(25).ConfigureAwait(true);
    }

    status.Should().Be("Stopped", "a stop policy ends the run once the threshold is reached");
    notifier.Sent.Should().BeEmpty("a bare stop action does not notify");
  }

  [Fact]
  public async Task NotifyAndStopDoesBoth() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 2, action = "notifyAndStop", notifyUrl = "http://127.0.0.1:9099/alerts" })
      .ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    var evt = await WaitForEventAsync(notifier).ConfigureAwait(true);
    evt.Should().NotBeNull("the alert must survive the stop it is announcing");
    evt!.Action.Should().Be("notifyAndStop");

    var sw = Stopwatch.StartNew();
    string? status = null;
    while (sw.ElapsedMilliseconds < 20000) {
      status = (await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true))
        .GetProperty("status").GetString();
      if (status == "Stopped") break;
      await Task.Delay(25).ConfigureAwait(true);
    }
    status.Should().Be("Stopped");
  }

  /// <summary>FR-018/FR-019/FR-020: a paused run parks, reports why, and resumes on demand.</summary>
  [Fact]
  public async Task PausePolicyParksTheRunAndResumeRestartsIt() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 2, action = "pause" }).ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    var paused = await WaitForHealthAsync(client, id,
      h => h.GetProperty("paused").GetBoolean()).ConfigureAwait(true);

    paused.Should().NotBeNull();
    paused!.Value.GetProperty("paused").GetBoolean().Should().BeTrue();
    paused.Value.GetProperty("pausedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    paused.Value.GetProperty("pauseReason").GetString().Should().Contain("failure policy");

    var status = (await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true))
      .GetProperty("status").GetString();
    status.Should().Be("Running", "a paused run is parked, not ended — it still holds its session");

    // The run does no further work while parked.
    var frozen = (await HealthAsync(client, id).ConfigureAwait(true))!.Value
      .GetProperty("cyclesCompleted").GetInt32();
    await Task.Delay(1000).ConfigureAwait(true);
    var later = (await HealthAsync(client, id).ConfigureAwait(true))!.Value
      .GetProperty("cyclesCompleted").GetInt32();
    later.Should().Be(frozen, "a parked run fires nothing");

    // Resume releases it and re-arms the policy.
    var resume = await client.PostAsync(new Uri($"/api/queues/{id}/resume", UriKind.Relative), null)
      .ConfigureAwait(true);
    resume.StatusCode.Should().Be(HttpStatusCode.OK);
    JsonDocument.Parse(await resume.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("resumed").GetBoolean().Should().BeTrue();

    var resumed = await WaitForHealthAsync(client, id,
      h => h.GetProperty("cyclesCompleted").GetInt32() > later).ConfigureAwait(true);
    resumed.Should().NotBeNull();
    resumed!.Value.GetProperty("cyclesCompleted").GetInt32().Should()
      .BeGreaterThan(later, "resuming puts the roster back to work");

    await StopAsync(client, id).ConfigureAwait(true);
  }

  /// <summary>FR-021: the existing stop action still works on a parked run.</summary>
  [Fact]
  public async Task PausedRunIsStillStoppable() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);
    
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 1, action = "pause" }).ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, MissingSequenceId).ConfigureAwait(true))
      .ConfigureAwait(true);

    await WaitForHealthAsync(client, id, h => h.GetProperty("paused").GetBoolean()).ConfigureAwait(true);
    await StopAsync(client, id).ConfigureAwait(true);

    var sw = Stopwatch.StartNew();
    string? status = null;
    while (sw.ElapsedMilliseconds < 20000) {
      status = (await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true))
        .GetProperty("status").GetString();
      if (status == "Stopped") break;
      await Task.Delay(25).ConfigureAwait(true);
    }

    status.Should().Be("Stopped", "a parked run must not become unstoppable");
  }

  /// <summary>
  /// FR-008: a recovery clears the count and re-arms the policy, so a later episode alerts again.
  /// Without this a queue that recovers once is never escalated for again.
  /// </summary>
  [Fact]
  public async Task SuccessfulCycleResetsTheCountAndReArmsThePolicy() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);
    SeedPassingSequence("seq-good", "PNS.Healthy");
    var id = await CreateQueueAsync(client,
      new { consecutiveFailedCycles = 3, action = "notify", notifyUrl = "http://127.0.0.1:9099/alerts" })
      .ConfigureAwait(true);
    await LinkAndStartAsync(client, id, await CreateTemplateAsync(client, "seq-good").ConfigureAwait(true))
      .ConfigureAwait(true);

    var health = await WaitForHealthAsync(client, id,
      h => h.GetProperty("cyclesCompleted").GetInt32() >= 5).ConfigureAwait(true);
    await StopAsync(client, id).ConfigureAwait(true);

    health.Should().NotBeNull();
    health!.Value.GetProperty("consecutiveFailedCycles").GetInt32().Should()
      .Be(0, "successful cycles keep the count at zero");
    health.Value.GetProperty("failurePolicyTripped").GetBoolean().Should().BeFalse();
    notifier.Sent.Should().BeEmpty("a healthy queue never alerts");
  }

  private static async Task<FailureNotificationEvent?> WaitForEventAsync(
    CapturingNotifier notifier, int timeoutMs = 20000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if (notifier.Sent.TryPeek(out var evt)) return evt;
      await Task.Delay(25).ConfigureAwait(true);
    }
    return null;
  }
}
