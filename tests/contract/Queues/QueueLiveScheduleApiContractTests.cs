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
/// Asserts the error/validation contract of POST /api/queues/{id}/live-schedule (feature 059):
/// 400 malformed/negative offset; 404 unknown queue or sequence; 409 when the queue has no active
/// run. None of these paths start a real run (they return before scheduling), so they don't pollute
/// the shared execution-log store. The 200 happy path + SC-004 (template unchanged) — which require
/// a live run — are covered in the integration project under ConfigIsolation with a clean data dir.
/// </summary>
public sealed class QueueLiveScheduleApiContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public QueueLiveScheduleApiContractTests() {
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

  private static async Task<string> CreateSequenceAsync(HttpClient client, string name) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative),
      new { name, steps = Array.Empty<string>() }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateQueueAsync(HttpClient client) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Farm", emulatorSerial = "emu-offline", cycleExecution = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  [Fact]
  public async Task LiveScheduleUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues/missing/live-schedule", UriKind.Relative),
      new { sequenceId = "seq-x", offset = "00:10:00" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Theory]
  [InlineData("5pm")]
  [InlineData("-00:10:00")]
  [InlineData("00:99:00")]
  public async Task LiveScheduleMalformedOrNegativeOffsetReturns400(string offset) {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/live-schedule", UriKind.Relative),
      new { sequenceId = "seq-x", offset }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
  }

  [Fact]
  public async Task LiveScheduleUnknownSequenceReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    // A valid offset on an existing (idle) queue, but an unknown sequence → 404 before scheduling.
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/live-schedule", UriKind.Relative),
      new { sequenceId = "does-not-exist", offset = "00:10:00" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task LiveScheduleWhenNotRunningReturns409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var seq = await CreateSequenceAsync(client, "Idle " + Guid.NewGuid().ToString("N")).ConfigureAwait(true);
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/live-schedule", UriKind.Relative),
      new { sequenceId = seq, offset = "00:10:00" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("not_running");
  }
}
