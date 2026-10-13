using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// CA2000: NewApp hands ownership of the factory to its caller, which disposes it with a
// `using var`. The constitution permits disabling code-quality rules in test code.
#pragma warning disable CA2000

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// The <c>notify</c> action step against a real run (feature 087, User Story 3): escalation that
/// lives inside a committed sequence rather than in service configuration.
/// <para>
/// The important assertion is that a failed delivery does <b>not</b> fail the step. A guard sequence
/// that has detected an unusable screen must still finish its own recovery logic even when the alert
/// cannot be sent — otherwise adding an alert to a guard would make the guard less reliable.
/// </para>
/// </summary>
[Collection("ConfigIsolation")]
public sealed class NotifyStepRunTests {
  public NotifyStepRunTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private sealed class CapturingNotifier : IFailureNotifier {
    public ConcurrentQueue<FailureNotificationEvent> Sent { get; } = new();
    public bool Succeeds { get; set; } = true;

    public Task<FailureNotificationResult> NotifyAsync(
      FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct = default) {
      Sent.Enqueue(evt);
      return Task.FromResult(new FailureNotificationResult(
        Succeeds, DateTimeOffset.Now, Succeeds ? null : "connection refused"));
    }
  }

  private static WebApplicationFactory<Program> NewApp(CapturingNotifier notifier) =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => {
        var existing = services.Where(d => d.ServiceType == typeof(IFailureNotifier)).ToList();
        foreach (var descriptor in existing) services.Remove(descriptor);
        services.AddSingleton<IFailureNotifier>(notifier);
      }));

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateNotifySequenceAsync(HttpClient client, string message) {
    var resp = await client.PostAsJsonAsync("/api/sequences", new {
      name = "Guard-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] {
        new {
          stepId = "alert",
          label = "Raise an alert",
          stepType = "Action",
          primitiveAction = new { type = "notify", schemaVersion = "1", payload = new { message } }
        }
      }
    }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return (await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true))
      .GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateQueueAsync(HttpClient client) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new {
      name = "Guarded farm", emulatorSerial = "emu-offline", cycleExecution = true
    }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateTemplateAsync(HttpClient client, string sequenceId) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new {
      name = "Tpl-" + Guid.NewGuid().ToString("N"),
      entries = new[] {
        new { sequenceId, scheduleType = "OncePerRun", timerTimeOfDay = (string?)null }
      },
      overwrite = false
    }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("id").GetString()!;
  }

  private static async Task LinkAndStartAsync(HttpClient client, string queueId, string templateId) {
    (await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative),
      new { templateId }).ConfigureAwait(true)).EnsureSuccessStatusCode();
    (await client.PostAsync(new Uri($"/api/queues/{queueId}/start", UriKind.Relative), null)
      .ConfigureAwait(true)).EnsureSuccessStatusCode();
  }

  private static async Task<FailureNotificationEvent?> WaitForEventAsync(
    CapturingNotifier notifier, int timeoutMs = 15000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if (notifier.Sent.TryPeek(out var evt)) return evt;
      await Task.Delay(25).ConfigureAwait(true);
    }
    return null;
  }

  private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path) {
    var resp = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  [Fact]
  public async Task NotifyStepRaisesTheAuthorsMessageWithQueueContext() {
    var notifier = new CapturingNotifier();
    using var app = NewApp(notifier);
    var client = NewClient(app);

    var seqId = await CreateNotifySequenceAsync(client, "Unrecognised screen; BACK did not dismiss it.")
      .ConfigureAwait(true);
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);
    await LinkAndStartAsync(client, queueId, await CreateTemplateAsync(client, seqId).ConfigureAwait(true))
      .ConfigureAwait(true);

    var evt = await WaitForEventAsync(notifier).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{queueId}/stop", UriKind.Relative), null).ConfigureAwait(true);

    evt.Should().NotBeNull();
    evt!.EventType.Should().Be(FailureNotificationEvent.SequenceNotifyEvent);
    evt.Message.Should().Be("Unrecognised screen; BACK did not dismiss it.",
      "the message is the author's, not one the service composed");
    evt.FailedSequenceId.Should().Be(seqId);
    evt.FailedSequenceName.Should().NotBeNullOrWhiteSpace();
    evt.QueueId.Should().Be(queueId, "a step running inside a queue carries that queue's context");
    evt.QueueName.Should().Be("Guarded farm");
    evt.EmulatorSerial.Should().Be("emu-offline");
    evt.Action.Should().BeNull("an author-raised alert took no policy action");
    evt.SchemaVersion.Should().Be(1);
  }

  /// <summary>
  /// FR-024. Adding an alert to a guard sequence must not make that guard more fragile, so a
  /// delivery failure leaves the step — and the cycle — successful.
  /// </summary>
  [Fact]
  public async Task FailedDeliveryDoesNotFailTheSequence() {
    var notifier = new CapturingNotifier { Succeeds = false };
    using var app = NewApp(notifier);
    var client = NewClient(app);

    var seqId = await CreateNotifySequenceAsync(client, "escalating").ConfigureAwait(true);
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);
    await LinkAndStartAsync(client, queueId, await CreateTemplateAsync(client, seqId).ConfigureAwait(true))
      .ConfigureAwait(true);

    await WaitForEventAsync(notifier).ConfigureAwait(true);

    // Let several cycles complete, then read the outcome the ledger recorded for them.
    JsonElement? health = null;
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 15000) {
      var root = await GetJsonAsync(client, $"/api/queues/{queueId}").ConfigureAwait(true);
      if (root.GetProperty("health").ValueKind == JsonValueKind.Object) {
        health = root.GetProperty("health").Clone();
        if (health.Value.GetProperty("cyclesCompleted").GetInt32() >= 3) break;
      }
      await Task.Delay(25).ConfigureAwait(true);
    }
    await client.PostAsync(new Uri($"/api/queues/{queueId}/stop", UriKind.Relative), null).ConfigureAwait(true);

    health.Should().NotBeNull();
    health!.Value.GetProperty("lastCycleStatus").GetString().Should()
      .Be("success", "an undeliverable alert must not fail the sequence that raised it");
    health.Value.GetProperty("consecutiveFailedCycles").GetInt32().Should().Be(0);
  }

  // ---- Feature 137: dedupe window -------------------------------------------------------------

  /// <summary>A real clock that tests can move forward. The local zone is UTC, so the window math is exact.</summary>
  private sealed class OffsetClock : TimeProvider {
    public TimeSpan Offset { get; set; }
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
  }

  /// <summary>A notifier that holds each delivery until the test opens the gate.</summary>
  private sealed class GatedNotifier : IFailureNotifier {
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls;
    public void Open() => _gate.TrySetResult();

    public async Task<FailureNotificationResult> NotifyAsync(
      FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct = default) {
      Interlocked.Increment(ref Calls);
      await _gate.Task.ConfigureAwait(false);
      return new FailureNotificationResult(true, DateTimeOffset.Now, null);
    }
  }

  private static WebApplicationFactory<Program> NewDedupeApp(IFailureNotifier notifier, OffsetClock clock) =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => {
        foreach (var d in services.Where(d => d.ServiceType == typeof(IFailureNotifier)).ToList()) services.Remove(d);
        services.AddSingleton(notifier);
        foreach (var d in services.Where(d => d.ServiceType == typeof(TimeProvider)).ToList()) services.Remove(d);
        services.AddSingleton<TimeProvider>(clock);
      }));

  private static async Task<string> SaveNotifySequenceAsync(IServiceProvider services, string id, params (string Key, string Value)[] extra) {
    var action = new SequenceActionPayload { Type = ActionTypes.Notify };
    action.Parameters["message"] = "Daily task timed out";
    foreach (var (key, value) in extra) action.Parameters[key] = value;
    var sequence = new CommandSequence { Id = id, Name = "Notify-" + id };
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "alert", StepType = SequenceStepType.Action, Action = action }
    });
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
    return id;
  }

  private static async Task<(string Outcome, string? Message)> RunAsync(IServiceProvider services, string sequenceId, string? queueId = null) {
    var context = queueId is null ? null : new ExecutionLogContext { OriginatingQueueId = queueId };
    var result = await services.GetRequiredService<ISequenceExecutionService>()
      .ExecuteAsync(sequenceId, sessionId: null, parentContext: context).ConfigureAwait(false);
    result.Status.Should().Be("Succeeded");
    var step = result.Steps.Single();
    return (step.ActionOutcome ?? string.Empty, step.Message);
  }

  [Fact]
  public async Task TenRunsInOneWindowSendOnce() {
    var notifier = new CapturingNotifier();
    var clock = new OffsetClock();
    using var app = NewDedupeApp(notifier, clock);
    _ = app.CreateClient();
    await SaveNotifySequenceAsync(app.Services, "s10", ("dedupeKey", "timeout"), ("within", "01:00:00")).ConfigureAwait(false);

    var outcomes = new System.Collections.Generic.List<string>();
    for (var i = 0; i < 10; i++) {
      outcomes.Add((await RunAsync(app.Services, "s10").ConfigureAwait(false)).Outcome);
      clock.Offset += TimeSpan.FromMinutes(1);
    }

    outcomes.Count(o => o == "sent").Should().Be(1);
    outcomes.Count(o => o == "suppressed").Should().Be(9);
    notifier.Sent.Should().HaveCount(1);
  }

  [Fact]
  public async Task NewWindowSendsAgain() {
    var notifier = new CapturingNotifier();
    var clock = new OffsetClock();
    using var app = NewDedupeApp(notifier, clock);
    _ = app.CreateClient();
    await SaveNotifySequenceAsync(app.Services, "swin", ("dedupeKey", "k"), ("within", "00:30:00")).ConfigureAwait(false);

    (await RunAsync(app.Services, "swin").ConfigureAwait(false)).Outcome.Should().Be("sent");
    clock.Offset += TimeSpan.FromMinutes(10);
    var suppressed = await RunAsync(app.Services, "swin").ConfigureAwait(false);
    suppressed.Outcome.Should().Be("suppressed");
    suppressed.Message.Should().Contain("notification suppressed: sent at");
    clock.Offset += TimeSpan.FromMinutes(40);
    (await RunAsync(app.Services, "swin").ConfigureAwait(false)).Outcome.Should().Be("sent");
    notifier.Sent.Should().HaveCount(2);
  }

  [Fact]
  public async Task SinceWindowStartsAtTheTimeOfDay() {
    var notifier = new CapturingNotifier();
    var clock = new OffsetClock();
    using var app = NewDedupeApp(notifier, clock);
    _ = app.CreateClient();
    // The window starts at the latest 00:00, so a run in the same UTC day is suppressed.
    await SaveNotifySequenceAsync(app.Services, "ssince", ("dedupeKey", "k"), ("since", "00:00")).ConfigureAwait(false);

    // Move the clock to 12:00 UTC of the next day, so the two runs below share one window.
    var now = DateTimeOffset.UtcNow;
    clock.Offset = now.Date.AddDays(1).AddHours(12) - now;
    (await RunAsync(app.Services, "ssince").ConfigureAwait(false)).Outcome.Should().Be("sent");
    clock.Offset += TimeSpan.FromHours(1);
    (await RunAsync(app.Services, "ssince").ConfigureAwait(false)).Outcome.Should().Be("suppressed");
    clock.Offset += TimeSpan.FromHours(13);
    (await RunAsync(app.Services, "ssince").ConfigureAwait(false)).Outcome.Should().Be("sent");
  }

  [Fact]
  public async Task FailedDeliveryDoesNotStartAWindow() {
    var notifier = new CapturingNotifier { Succeeds = false };
    var clock = new OffsetClock();
    using var app = NewDedupeApp(notifier, clock);
    _ = app.CreateClient();
    await SaveNotifySequenceAsync(app.Services, "sfail", ("dedupeKey", "k"), ("within", "01:00:00")).ConfigureAwait(false);

    var first = await RunAsync(app.Services, "sfail").ConfigureAwait(false);
    first.Outcome.Should().Be("success");
    first.Message.Should().Contain("notification not delivered");

    notifier.Succeeds = true;
    (await RunAsync(app.Services, "sfail").ConfigureAwait(false)).Outcome.Should().Be("sent");
    notifier.Sent.Should().HaveCount(2);
  }

  [Fact]
  public async Task RestartKeepsTheWindow() {
    var notifier = new CapturingNotifier();
    var clock = new OffsetClock();
    using var app = NewDedupeApp(notifier, clock);
    _ = app.CreateClient();
    await SaveNotifySequenceAsync(app.Services, "srestart", ("dedupeKey", "k"), ("within", "01:00:00")).ConfigureAwait(false);
    (await RunAsync(app.Services, "srestart").ConfigureAwait(false)).Outcome.Should().Be("sent");

    var store = (GameBot.Domain.Queues.FileSentAlertStore)app.Services.GetRequiredService<GameBot.Domain.Queues.ISentAlertStore>();
    var dataRoot = System.IO.Directory.GetParent(store.FolderPath)!.FullName;
    using var reloaded = new GameBot.Domain.Queues.FileSentAlertStore(dataRoot);
    (await reloaded.WasSentSinceAsync(GameBot.Domain.Commands.Notify.NotifyPayload.AdhocScope, "k", DateTimeOffset.Now.AddHours(-1)).ConfigureAwait(false))
      .Should().NotBeNull("the record is on disk");
  }

  [Fact]
  public async Task ParallelRunsSendOnceAndLiveReservationSuppresses() {
    var notifier = new GatedNotifier();
    var clock = new OffsetClock();
    using var app = NewDedupeApp(notifier, clock);
    _ = app.CreateClient();
    await SaveNotifySequenceAsync(app.Services, "spar", ("dedupeKey", "k"), ("within", "01:00:00")).ConfigureAwait(false);

    var tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(() => RunAsync(app.Services, "spar"))).ToList();
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 15000 && tasks.Count(t => t.IsCompleted) < 2) await Task.Delay(20).ConfigureAwait(false);

    tasks.Count(t => t.IsCompleted).Should().Be(2, "two runs are suppressed while the first run holds the slot");
    notifier.Open();
    var results = await Task.WhenAll(tasks).ConfigureAwait(false);

    results.Count(r => r.Outcome == "sent").Should().Be(1);
    results.Count(r => r.Outcome == "suppressed").Should().Be(2);
    notifier.Calls.Should().Be(1);
  }

  [Fact]
  public async Task ScopeIsQueueAndKeyAndQueueDeleteClearsIt() {
    var notifier = new CapturingNotifier();
    var clock = new OffsetClock();
    using var app = NewDedupeApp(notifier, clock);
    var client = NewClient(app);
    await SaveNotifySequenceAsync(app.Services, "sa", ("dedupeKey", "a"), ("within", "01:00:00")).ConfigureAwait(false);
    await SaveNotifySequenceAsync(app.Services, "sb", ("dedupeKey", "b"), ("within", "01:00:00")).ConfigureAwait(false);
    var q1 = await CreateQueueAsync(client).ConfigureAwait(true);
    var q2 = await CreateQueueAsync(client).ConfigureAwait(true);

    (await RunAsync(app.Services, "sa", q1).ConfigureAwait(true)).Outcome.Should().Be("sent");
    (await RunAsync(app.Services, "sa", q2).ConfigureAwait(true)).Outcome.Should().Be("sent", "another queue is another scope");
    (await RunAsync(app.Services, "sb", q1).ConfigureAwait(true)).Outcome.Should().Be("sent", "another key is another scope");
    (await RunAsync(app.Services, "sa").ConfigureAwait(true)).Outcome.Should().Be("sent", "ad hoc is its own scope");
    (await RunAsync(app.Services, "sa", q1).ConfigureAwait(true)).Outcome.Should().Be("suppressed");

    (await client.DeleteAsync(new Uri($"/api/queues/{q1}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    var store = app.Services.GetRequiredService<GameBot.Domain.Queues.ISentAlertStore>();
    (await store.WasSentSinceAsync(q1, "a", DateTimeOffset.MinValue).ConfigureAwait(true)).Should().BeNull();
    (await store.WasSentSinceAsync(q2, "a", DateTimeOffset.MinValue).ConfigureAwait(true)).Should().NotBeNull();
  }

  [Fact]
  public async Task StepWithoutDedupeFieldsSendsEveryTime() {
    var notifier = new CapturingNotifier();
    var clock = new OffsetClock();
    using var app = NewDedupeApp(notifier, clock);
    _ = app.CreateClient();
    await SaveNotifySequenceAsync(app.Services, "sold").ConfigureAwait(false);

    (await RunAsync(app.Services, "sold").ConfigureAwait(false)).Should().Be(("success", "notification delivered"));
    (await RunAsync(app.Services, "sold").ConfigureAwait(false)).Should().Be(("success", "notification delivered"));
    notifier.Sent.Should().HaveCount(2);
  }
}
