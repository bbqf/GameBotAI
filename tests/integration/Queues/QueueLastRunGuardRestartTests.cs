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

// CA2000: NewApp gives the factory to its caller, which disposes it.
#pragma warning disable CA2000, CA2007

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 105 (SC-002, User Story 2 scenario 6): a daily task with a <c>lastRun</c> guard does its
/// work one time in each window, also after a queue restart and a service restart.
/// <para>
/// The work step is a <c>notify</c> action. The test replaces <see cref="IFailureNotifier"/> with a
/// recorder, so each run of the work step is one recorded event (analyze finding U1).
/// </para>
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueLastRunGuardRestartTests {
  private static readonly DateTimeOffset Day = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

  public QueueLastRunGuardRestartTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  /// <summary>A clock that moves only when the test moves it. Same shape as the unit-test FakeTimeProvider.</summary>
  private sealed class TestClock : TimeProvider {
    private DateTimeOffset _utcNow;

    public TestClock(DateTimeOffset start) => _utcNow = start;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
  }

  private sealed class RecordingNotifier : IFailureNotifier {
    public ConcurrentQueue<FailureNotificationEvent> Sent { get; } = new();

    public Task<FailureNotificationResult> NotifyAsync(FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct = default) {
      Sent.Enqueue(evt);
      return Task.FromResult(new FailureNotificationResult(true, DateTimeOffset.Now, null));
    }

    public int WorkRuns => Sent.Count(e => e.EventType == FailureNotificationEvent.SequenceNotifyEvent);
  }

  private static WebApplicationFactory<Program> NewApp(TestClock clock, RecordingNotifier notifier) =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(TimeProvider)).ToList()) services.Remove(descriptor);
        services.AddSingleton<TimeProvider>(clock);
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IFailureNotifier)).ToList()) services.Remove(descriptor);
        services.AddSingleton<IFailureNotifier>(notifier);
      }));

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> ReadIdAsync(HttpResponseMessage response) {
    var body = await response.Content.ReadAsStringAsync();
    response.IsSuccessStatusCode.Should().BeTrue(body);
    return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
  }

  /// <summary>
  /// A count loop of one iteration: a Break step with the guard, then the work step. When the guard
  /// is true, the Break ends the loop and the work does not run.
  /// </summary>
  private static async Task<string> CreateGuardedSequenceAsync(HttpClient client) =>
    await ReadIdAsync(await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new {
      name = "Daily guarded " + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] {
        new {
          stepId = "once",
          stepType = "Loop",
          loop = new { loopType = "count", count = 1 },
          body = new object[] {
            new {
              stepId = "guard",
              stepType = "Break",
              breakCondition = new { type = "lastRun", sequence = "self", status = "success", since = "11:00" }
            },
            new {
              stepId = "work",
              stepType = "Action",
              primitiveAction = new { type = "notify", schemaVersion = "1", payload = new { message = "daily work ran" } }
            }
          }
        }
      }
    }));

  private static async Task<string> CreateQueueAsync(HttpClient client, string sequenceId) {
    var queueId = await ReadIdAsync(await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Daily " + Guid.NewGuid().ToString("N"), emulatorSerial = "emu-lastrun", cycleExecution = false }));
    var templateId = await ReadIdAsync(await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new {
      name = "Tpl-" + Guid.NewGuid().ToString("N"),
      entries = new[] { new { sequenceId, scheduleType = "OncePerRun", timerTimeOfDay = (string?)null } },
      overwrite = false
    }));
    (await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId }))
      .EnsureSuccessStatusCode();
    return queueId;
  }

  private static async Task<JsonElement> GetQueueAsync(HttpClient client, string queueId) {
    var response = await client.GetAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
  }

  /// <summary>Starts the queue and waits until the one-pass run stops by itself.</summary>
  private static async Task RunOnceAsync(HttpClient client, string queueId) {
    var start = await client.PostAsync(new Uri($"/api/queues/{queueId}/start", UriKind.Relative), null);
    start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

    var sw = Stopwatch.StartNew();
    string? status = null;
    while (sw.ElapsedMilliseconds < 20000) {
      status = (await GetQueueAsync(client, queueId)).GetProperty("status").GetString();
      if (status == "Stopped") break;
      await Task.Delay(25);
    }

    status.Should().Be("Stopped");
  }

  [Fact]
  public async Task TheGuardedWorkRunsOneTimeInTheWindowAcrossRestarts() {
    var notifier = new RecordingNotifier();
    string queueId;
    string sequenceId;

    // Application instance 1 at 13:00: create the sequence, the queue and the template. No run.
    using (var app1 = NewApp(new TestClock(Day.AddHours(13)), notifier)) {
      var client = NewClient(app1);
      sequenceId = await CreateGuardedSequenceAsync(client);
      queueId = await CreateQueueAsync(client, sequenceId);
    }

    // Instance 2 at 14:00: no success since 11:00, so the guard is false and the work runs.
    using (var app2 = NewApp(new TestClock(Day.AddHours(14)), notifier)) {
      await RunOnceAsync(NewClient(app2), queueId);
    }

    notifier.WorkRuns.Should().Be(1);

    // Instance 3 at 15:00 (a service restart: a new store instance on the same data folder). The
    // success at 14:00 is in the window, so the guard is true in both runs and the work does not run.
    var clock = new TestClock(Day.AddHours(15));
    using var app3 = NewApp(clock, notifier);
    var client3 = NewClient(app3);
    await RunOnceAsync(client3, queueId);
    clock.Advance(TimeSpan.FromHours(1));
    await RunOnceAsync(client3, queueId);

    notifier.WorkRuns.Should().Be(1, "the work ran exactly one time in the window");

    var stats = (await GetQueueAsync(client3, queueId)).GetProperty("sequenceStats").GetProperty(sequenceId);
    stats.GetProperty("successCount").GetInt64().Should().Be(3);
    stats.GetProperty("lastRunStatus").GetString().Should().Be("success");
    stats.GetProperty("lastRunEndedAt").GetDateTimeOffset().Should().Be(Day.AddHours(16));
  }
}
