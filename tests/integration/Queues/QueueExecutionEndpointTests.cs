using System;
using System.Diagnostics;
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
/// Exercises the real queue execution engine through the HTTP endpoints. ADB runs in stub mode
/// (GAMEBOT_USE_ADB=false), so a cycling queue is used to hold a deterministic "Running" window.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueExecutionEndpointTests {
  public QueueExecutionEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateQueueAsync(HttpClient client, string serial = "emu-offline", bool cycle = false) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { name = "Farm", emulatorSerial = serial, cycleExecution = cycle }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateTemplateAsync(HttpClient client, params string[] sequenceIds) {
    var entries = Array.ConvertAll(sequenceIds, id => new { sequenceId = id });
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new { name = "Tpl-" + Guid.NewGuid().ToString("N"), entries, overwrite = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static Task<HttpResponseMessage> LinkTemplateAsync(HttpClient client, string queueId, string templateId)
    => client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId });

  private static async Task<string> StatusAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
    var detail = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    return detail.GetProperty("status").GetString()!;
  }

  private static async Task WaitForStatusAsync(HttpClient client, string id, string expected, int timeoutMs = 5000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if (await StatusAsync(client, id).ConfigureAwait(true) == expected) return;
      await Task.Delay(25).ConfigureAwait(true);
    }
  }

  // Starts a cycle-execution queue linked to a one-entry template so the run loops and the queue
  // stays Running deterministically until stopped (or the app is disposed).
  private static async Task<string> StartCyclingQueueAsync(HttpClient client) {
    var id = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    var tpl = await CreateTemplateAsync(client, "seq-loop").ConfigureAwait(true);
    await LinkTemplateAsync(client, id, tpl).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, id, "Running").ConfigureAwait(true);
    return id;
  }

  private static Task<HttpResponseMessage> StopAsync(HttpClient client, string id)
    => client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null);

  [Fact]
  public async Task StartUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    (await client.PostAsync(new Uri("/api/queues/missing/start", UriKind.Relative), null).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task StopUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    (await client.PostAsync(new Uri("/api/queues/missing/stop", UriKind.Relative), null).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task StartWithoutLinkedTemplateSettlesStopped() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    // No template to run → the run fails fast and the queue returns to Stopped.
    await WaitForStatusAsync(client, id, "Stopped").ConfigureAwait(true);
    (await StatusAsync(client, id).ConfigureAwait(true)).Should().Be("Stopped");
  }

  [Fact]
  public async Task StopNotRunningQueueIsNoOp() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await StopAsync(client, id).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    (await StatusAsync(client, id).ConfigureAwait(true)).Should().Be("Stopped");
  }

  [Fact]
  public async Task StartingAnAlreadyRunningQueueReturns409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await StartCyclingQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Should().Contain("already_running");

    await StopAsync(client, id).ConfigureAwait(true);
  }

  [Fact]
  public async Task StopReturnsRunningQueueToStopped() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await StartCyclingQueueAsync(client).ConfigureAwait(true);

    var resp = await StopAsync(client, id).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    await WaitForStatusAsync(client, id, "Stopped").ConfigureAwait(true);
    (await StatusAsync(client, id).ConfigureAwait(true)).Should().Be("Stopped");
  }

  [Fact]
  public async Task UpdateWhileRunningReturns409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await StartCyclingQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PutAsJsonAsync(new Uri($"/api/queues/{id}", UriKind.Relative), new { name = "X", cycleExecution = false }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Should().Contain("queue_running");

    await StopAsync(client, id).ConfigureAwait(true);
  }

  [Fact]
  public async Task DeleteWhileRunningReturns409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await StartCyclingQueueAsync(client).ConfigureAwait(true);

    (await client.DeleteAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.Conflict);

    await StopAsync(client, id).ConfigureAwait(true);
  }

  [Fact]
  public async Task AddEntryAllowedWhileRunning() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await StartCyclingQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "seq-a" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);

    await StopAsync(client, id).ConfigureAwait(true);
  }

  // feature 059: live relative scheduling against a running queue (200 happy path + SC-004).

  private static async Task<string> CreateSequenceAsync(HttpClient client, string name) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative),
      new { name, steps = Array.Empty<string>() }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> TemplateJsonAsync(HttpClient client, string templateId) {
    var resp = await client.GetAsync(new Uri($"/api/queue-templates/{templateId}", UriKind.Relative)).ConfigureAwait(true);
    return await resp.Content.ReadAsStringAsync().ConfigureAwait(true);
  }

  [Fact]
  public async Task LiveScheduleAgainstRunningQueueReturns200AndLeavesTemplateUnchanged() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    // A running cycling queue linked to a template, plus a real target sequence in the library.
    var queueId = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    var templateId = await CreateTemplateAsync(client, "seq-loop").ConfigureAwait(true);
    await LinkTemplateAsync(client, queueId, templateId).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{queueId}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, queueId, "Running").ConfigureAwait(true);

    var seq = await CreateSequenceAsync(client, "Live " + Guid.NewGuid().ToString("N")).ConfigureAwait(true);
    var templateBefore = await TemplateJsonAsync(client, templateId).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/live-schedule", UriKind.Relative),
      new { sequenceId = seq, offset = "00:10:00" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    body.GetProperty("sequenceId").GetString().Should().Be(seq);
    body.GetProperty("offset").GetString().Should().Be("00:10:00");
    body.TryGetProperty("expectedFireAt", out var fireAt).Should().BeTrue();
    fireAt.ValueKind.Should().Be(JsonValueKind.String);

    // SC-004: the live schedule is in-memory only; the linked template is untouched.
    var templateAfter = await TemplateJsonAsync(client, templateId).ConfigureAwait(true);
    templateAfter.Should().Be(templateBefore);

    await StopAsync(client, queueId).ConfigureAwait(true);
  }
}
