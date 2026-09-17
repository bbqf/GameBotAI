using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// CA2000: the hand-built handle's token source lives as long as the test and is disposed in finally.
#pragma warning disable CA2000

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 096 (issue #199): during an idle pause <c>GET /api/queues/{id}</c> reported
/// <c>health.paused: false</c> while <c>/monitor</c> reported <c>IdlePause</c> for the same queue.
/// <para>
/// A real idle pause needs a device session, so these tests register a hand-built run handle in the
/// live host's run registry and mark the queue Running — the exact state the run loop publishes — and
/// then read both endpoints through the real HTTP pipeline and serializer.
/// </para>
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueHealthIdlePauseTests {
  private static readonly DateTimeOffset PausedAt = new(2026, 9, 16, 20, 57, 36, TimeSpan.FromHours(2));
  private static readonly DateTimeOffset ResumeAt = new(2026, 9, 16, 20, 59, 6, TimeSpan.FromHours(2));

  public QueueHealthIdlePauseTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateQueueAsync(HttpClient client) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new {
      name = "Idle farm",
      emulatorSerial = "emu-offline",
      cycleExecution = false,
      pauseWhenIdle = true,
      idleThresholdSeconds = 30
    }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path) {
    var resp = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  private static async Task<JsonElement> HealthAsync(HttpClient client, string id) {
    var health = (await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true)).GetProperty("health");
    health.ValueKind.Should().Be(JsonValueKind.Object, "a Running queue with a run handle carries a health block");
    return health;
  }

  private static async Task<string?> MonitorCurrentKindAsync(HttpClient client, string id) {
    var current = (await GetJsonAsync(client, $"/api/queues/{id}/monitor").ConfigureAwait(true)).GetProperty("current");
    return current.ValueKind == JsonValueKind.Object ? current.GetProperty("scheduleKind").GetString() : null;
  }

  /// <summary>Registers <paramref name="handle"/> as the queue's live run for the duration of <paramref name="body"/>.</summary>
  private static async Task WithLiveRunAsync(
    WebApplicationFactory<Program> app, string id, QueueRunHandle handle, Func<Task> body) {
    var registry = app.Services.GetRequiredService<IQueueRunRegistry>();
    var runtime = app.Services.GetRequiredService<IQueueRuntimeStore>();
    registry.TryAdd(id, handle).Should().BeTrue();
    runtime.SetStatus(id, QueueExecutionStatus.Running);
    try {
      await body().ConfigureAwait(true);
    }
    finally {
      runtime.SetStatus(id, QueueExecutionStatus.Stopped);
      registry.Remove(id, out _);
      handle.Cts.Dispose();
    }
  }

  [Fact]
  public async Task HealthReportsAnIdlePauseAndAgreesWithTheMonitor() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      // Not paused yet: nothing reported, and the monitor has no Idle Pause item either.
      var before = await HealthAsync(client, id).ConfigureAwait(true);
      before.GetProperty("paused").GetBoolean().Should().BeFalse();
      before.GetProperty("pausedAt").ValueKind.Should().Be(JsonValueKind.Null);
      before.GetProperty("pauseReason").ValueKind.Should().Be(JsonValueKind.Null);
      before.GetProperty("pauseKind").ValueKind.Should().Be(JsonValueKind.Null);
      (await MonitorCurrentKindAsync(client, id).ConfigureAwait(true)).Should().NotBe("IdlePause");

      handle.EnterIdlePause(ResumeAt, PausedAt);

      var paused = await HealthAsync(client, id).ConfigureAwait(true);
      (await MonitorCurrentKindAsync(client, id).ConfigureAwait(true)).Should().Be("IdlePause");
      paused.GetProperty("paused").GetBoolean().Should().BeTrue("the monitor reports IdlePause for this run");
      paused.GetProperty("pausedAt").GetDateTimeOffset().Should().Be(PausedAt);
      paused.GetProperty("pauseReason").GetString().Should().Be("idle pause: resumes at 20:59");
      paused.GetProperty("pauseKind").GetString().Should().Be("idle");
      paused.GetProperty("failurePolicyTripped").GetBoolean().Should().BeFalse();

      handle.ClearIdlePause();

      var after = await HealthAsync(client, id).ConfigureAwait(true);
      (await MonitorCurrentKindAsync(client, id).ConfigureAwait(true)).Should().NotBe("IdlePause");
      after.GetProperty("paused").GetBoolean().Should().BeFalse();
      after.GetProperty("pausedAt").ValueKind.Should().Be(JsonValueKind.Null);
      after.GetProperty("pauseReason").ValueKind.Should().Be(JsonValueKind.Null);
      after.GetProperty("pauseKind").ValueKind.Should().Be(JsonValueKind.Null);
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task ResumeDoesNotEndAnIdlePause() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      handle.EnterIdlePause(ResumeAt, PausedAt);

      var resume = await client.PostAsync(new Uri($"/api/queues/{id}/resume", UriKind.Relative), null)
        .ConfigureAwait(true);
      resume.StatusCode.Should().Be(HttpStatusCode.OK);
      JsonDocument.Parse(await resume.Content.ReadAsStringAsync().ConfigureAwait(true))
        .RootElement.GetProperty("resumed").GetBoolean().Should().BeFalse("resume releases only a failure-policy pause");

      var health = await HealthAsync(client, id).ConfigureAwait(true);
      health.GetProperty("paused").GetBoolean().Should().BeTrue();
      health.GetProperty("pauseKind").GetString().Should().Be("idle");
      health.GetProperty("failurePolicyTripped").GetBoolean().Should().BeFalse();
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task FailurePolicyPauseIsReportedOverAnIdlePause() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      handle.EnterIdlePause(ResumeAt, PausedAt);
      var policyAt = PausedAt + TimeSpan.FromSeconds(10);
      handle.EnterPolicyPause("failure policy: 2 consecutive failed cycles", policyAt);

      var health = await HealthAsync(client, id).ConfigureAwait(true);
      health.GetProperty("paused").GetBoolean().Should().BeTrue();
      health.GetProperty("pauseKind").GetString().Should().Be("failurePolicy");
      health.GetProperty("pausedAt").GetDateTimeOffset().Should().Be(policyAt);
      health.GetProperty("pauseReason").GetString().Should().Be("failure policy: 2 consecutive failed cycles");
    }).ConfigureAwait(true);
  }
}
