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
/// Feature 171: <c>POST /api/queues/{id}/pause</c>, the extended <c>POST /api/queues/{id}/resume</c>, and the
/// <c>operatorPause</c> member of the health block. A real pause needs a run loop at a gate, so these tests register a
/// hand-built run handle and move its register the way the run loop does.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueOperatorPauseEndpointTests {
  private static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

  public QueueOperatorPauseEndpointTests() {
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
      name = "Pause farm",
      emulatorSerial = "emu-offline",
      cycleExecution = true
    }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> BodyAsync(HttpResponseMessage resp) =>
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();

  private static async Task<JsonElement> HealthAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("health");
  }

  private static Task<HttpResponseMessage> PostAsync(HttpClient client, string id, string verb) =>
    client.PostAsync(new Uri($"/api/queues/{id}/{verb}", UriKind.Relative), null);

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
  public async Task PauseOfAnUnknownQueueGives404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await PostAsync(client, "nope", "pause").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task PauseOfAQueueThatDoesNotRunGives409WithAHint() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await PostAsync(client, id, "pause").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var error = (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("not_running");
    error.GetProperty("hint").GetString().Should().Be("Start the queue first.");
  }

  [Fact]
  public async Task PauseStartsAsPausingAndHealthSaysSo() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      (await HealthAsync(client, id).ConfigureAwait(true)).GetProperty("operatorPause").GetString().Should().Be("none");

      var resp = await PostAsync(client, id, "pause").ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.OK);
      var body = await BodyAsync(resp).ConfigureAwait(true);
      body.GetProperty("operatorPause").GetString().Should().Be("pausing");
      var health = await HealthAsync(client, id).ConfigureAwait(true);
      health.GetProperty("operatorPause").GetString().Should().Be("pausing");
      health.GetProperty("paused").GetBoolean().Should().BeFalse("a queue that is pausing is not paused yet");
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task ARepeatPauseRequestIsSafe() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      var first = await BodyAsync(await PostAsync(client, id, "pause").ConfigureAwait(true)).ConfigureAwait(true);
      var second = await PostAsync(client, id, "pause").ConfigureAwait(true);

      second.StatusCode.Should().Be(HttpStatusCode.OK);
      var secondBody = await BodyAsync(second).ConfigureAwait(true);
      secondBody.GetProperty("requestedAt").GetDateTimeOffset().Should().Be(first.GetProperty("requestedAt").GetDateTimeOffset(), "the first time stays");
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task WhenTheLoopReachesTheGateHealthReportsAnOperatorPause() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      await PostAsync(client, id, "pause").ConfigureAwait(true);
      var wait = handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);

      var health = await HealthAsync(client, id).ConfigureAwait(true);

      health.GetProperty("operatorPause").GetString().Should().Be("paused");
      health.GetProperty("paused").GetBoolean().Should().BeTrue();
      health.GetProperty("pauseKind").GetString().Should().Be("operator");
      health.GetProperty("pausedAt").GetDateTimeOffset().Should().Be(Start);
      health.GetProperty("pauseReason").GetString().Should().Be("operator pause: waiting for resume");

      var resume = await PostAsync(client, id, "resume").ConfigureAwait(true);
      var body = await BodyAsync(resume).ConfigureAwait(true);
      body.GetProperty("resumed").GetBoolean().Should().BeTrue();
      body.GetProperty("kind").GetString().Should().Be("operator");
      await wait.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
      var after = await HealthAsync(client, id).ConfigureAwait(true);
      after.GetProperty("operatorPause").GetString().Should().Be("none");
      after.GetProperty("paused").GetBoolean().Should().BeFalse();
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task ResumeWhilePausingCancelsTheRequest() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      await PostAsync(client, id, "pause").ConfigureAwait(true);

      var resume = await PostAsync(client, id, "resume").ConfigureAwait(true);

      var body = await BodyAsync(resume).ConfigureAwait(true);
      body.GetProperty("resumed").GetBoolean().Should().BeTrue();
      body.GetProperty("kind").GetString().Should().Be("operator");
      handle.OperatorPause.Should().Be(OperatorPauseState.None);
      (await HealthAsync(client, id).ConfigureAwait(true)).GetProperty("operatorPause").GetString().Should().Be("none");
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task ResumeWithNothingToReleaseChangesNothing() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      var resume = await PostAsync(client, id, "resume").ConfigureAwait(true);

      resume.StatusCode.Should().Be(HttpStatusCode.OK);
      var body = await BodyAsync(resume).ConfigureAwait(true);
      body.GetProperty("resumed").GetBoolean().Should().BeFalse();
      body.TryGetProperty("kind", out _).Should().BeFalse();
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task WhenBothPausesAreInForceTheFirstResumeEndsTheOperatorPauseAndTheSecondEndsThePolicyPause() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = id, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, id, handle, async () => {
      handle.EnterPolicyPause("failure policy: three failed cycles", Start);
      (await PostAsync(client, id, "pause").ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.OK);
      _ = handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);

      var health = await HealthAsync(client, id).ConfigureAwait(true);
      health.GetProperty("pauseKind").GetString().Should().Be("failurePolicy", "the failure-policy pause is reported first");
      health.GetProperty("operatorPause").GetString().Should().Be("paused");

      var first = await BodyAsync(await PostAsync(client, id, "resume").ConfigureAwait(true)).ConfigureAwait(true);
      first.GetProperty("kind").GetString().Should().Be("operator");
      handle.IsPolicyPaused.Should().BeTrue();

      var second = await BodyAsync(await PostAsync(client, id, "resume").ConfigureAwait(true)).ConfigureAwait(true);
      second.GetProperty("kind").GetString().Should().Be("failurePolicy");
      handle.IsPolicyPaused.Should().BeFalse();

      var third = await BodyAsync(await PostAsync(client, id, "resume").ConfigureAwait(true)).ConfigureAwait(true);
      third.GetProperty("resumed").GetBoolean().Should().BeFalse();
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task OpenApiDescribesThePauseEndpointAndTheHealthMember() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true);
    var doc = await BodyAsync(resp).ConfigureAwait(true);

    doc.GetProperty("paths").TryGetProperty("/api/queues/{id}/pause", out _).Should().BeTrue();
    var health = doc.GetProperty("components").GetProperty("schemas").GetProperty("QueueHealthResponse").GetProperty("properties");
    health.GetProperty("operatorPause").GetProperty("description").GetString().Should().Contain("Pause when idle");
    health.GetProperty("pauseKind").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).Should().Contain("operator");
  }
}
