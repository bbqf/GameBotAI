using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Asserts the not-running contract of the queue observability surface (feature 086, issue #180):
/// GET /api/queues/{id}/cycles is 404 for an unknown queue and 200 with running:false for a known but
/// stopped one, and GET /api/queues/{id} carries no health block unless the queue is actually running.
/// <para>
/// None of these paths start a real run, so they do not write to the execution-log store this project
/// shares across test classes. The live-run assertions — populated health, per-entry cycle outcomes,
/// the climbing consecutive-failure count — live in the integration project, which gets a clean data
/// dir per class.
/// </para>
/// </summary>
public sealed class QueueCyclesApiContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public QueueCyclesApiContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    GC.SuppressFinalize(this);
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateQueueAsync(HttpClient client) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Cyc", emulatorSerial = "emu-offline", cycleExecution = true }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path) {
    var resp = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  [Fact]
  public async Task CyclesForUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.GetAsync(new Uri("/api/queues/missing/cycles", UriKind.Relative)).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task CyclesForStoppedQueueReturns200EmptyNotAnError() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var root = await GetJsonAsync(client, $"/api/queues/{id}/cycles").ConfigureAwait(true);

    root.GetProperty("queueId").GetString().Should().Be(id);
    root.GetProperty("running").GetBoolean().Should().BeFalse();
    root.GetProperty("cycles").GetArrayLength().Should()
      .Be(0, "a queue that has never run returns an empty list, not an error");
  }

  [Fact]
  public async Task StoppedQueueHasNoHealthBlock() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);

    root.GetProperty("status").GetString().Should().Be("Stopped");
    root.GetProperty("health").ValueKind.Should()
      .Be(JsonValueKind.Null, "a zeroed health block could be misread as a live run that has done nothing");
  }

  [Fact]
  public async Task HealthAndCyclesAgreeAboutWhetherARunIsInProgress() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var detail = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);
    var cycles = await GetJsonAsync(client, $"/api/queues/{id}/cycles").ConfigureAwait(true);

    var healthPresent = detail.GetProperty("health").ValueKind != JsonValueKind.Null;
    var running = cycles.GetProperty("running").GetBoolean();
    healthPresent.Should().Be(running, "both read paths gate on the same liveness condition");
    (detail.GetProperty("status").GetString() == "Running").Should()
      .Be(healthPresent, "a response must never report Stopped while carrying a health block");
  }

  [Theory]
  [InlineData("?limit=0")]
  [InlineData("?limit=-1")]
  [InlineData("?limit=9999")]
  [InlineData("")]
  public async Task OutOfRangeLimitIsClampedNotRejected(string query) {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.GetAsync(new Uri($"/api/queues/{id}/cycles{query}", UriKind.Relative)).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK, "a careless limit still gets an answer");
  }

  [Fact]
  public async Task QueueListResponseIsUnchangedByThisFeature() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    await CreateQueueAsync(client).ConfigureAwait(true);

    var root = await GetJsonAsync(client, "/api/queues").ConfigureAwait(true);

    root.ValueKind.Should().Be(JsonValueKind.Array);
    foreach (var item in root.EnumerateArray()) {
      item.TryGetProperty("health", out _).Should()
        .BeFalse("health is on the single-queue read only; the list keeps its existing shape and cost");
    }
  }
}
