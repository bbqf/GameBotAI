using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

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

  private static async Task<string> CreateQueueAsync(HttpClient client, string serial = "emu-offline") {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { name = "Farm", emulatorSerial = serial }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> StatusAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
    var detail = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    return detail.GetProperty("status").GetString()!;
  }

  [Fact]
  public async Task StartSetsRunningEvenWhenEmulatorNotConnected() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client, serial: "never-connected").ConfigureAwait(true);

    var resp = await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    (await StatusAsync(client, id).ConfigureAwait(true)).Should().Be("Running");
  }

  [Fact]
  public async Task StartIsIdempotent() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    (await StatusAsync(client, id).ConfigureAwait(true)).Should().Be("Running");
  }

  [Fact]
  public async Task StopReturnsToStoppedIdempotently() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
    (await StatusAsync(client, id).ConfigureAwait(true)).Should().Be("Stopped");
  }

  [Fact]
  public async Task UpdateWhileRunningReturns409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);

    var resp = await client.PutAsJsonAsync(new Uri($"/api/queues/{id}", UriKind.Relative), new { name = "X", cycleExecution = false }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Should().Contain("queue_running");
  }

  [Fact]
  public async Task DeleteWhileRunningReturns409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);

    (await client.DeleteAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.Conflict);
  }

  [Fact]
  public async Task AddEntryAllowedWhileRunning() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "seq-a" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  [Fact]
  public async Task StartUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    (await client.PostAsync(new Uri("/api/queues/missing/start", UriKind.Relative), null).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }
}
