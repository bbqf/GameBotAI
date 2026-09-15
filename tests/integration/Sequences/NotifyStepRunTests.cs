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
using GameBot.Service.Services.Notifications;
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
}
