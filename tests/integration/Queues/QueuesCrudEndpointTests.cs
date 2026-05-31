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
public sealed class QueuesCrudEndpointTests {
  public QueuesCrudEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<JsonElement> CreateQueueAsync(HttpClient client, string name = "Farm", string serial = "emu-1", bool cycle = false) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { name, emulatorSerial = serial, cycleExecution = cycle }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  [Fact]
  public async Task CreateReturnsStoppedStatusAndZeroEntries() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var created = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    created.GetProperty("name").GetString().Should().Be("Farm");
    created.GetProperty("emulatorSerial").GetString().Should().Be("emu-1");
    created.GetProperty("cycleExecution").GetBoolean().Should().BeTrue();
    created.GetProperty("status").GetString().Should().Be("Stopped");
    created.GetProperty("entryCount").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task CreateWithoutNameReturns400WithFieldMessage() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { emulatorSerial = "emu-1" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Should().Contain("name is required");
  }

  [Fact]
  public async Task CreateWithoutEmulatorReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { name = "Farm" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Should().Contain("emulatorSerial is required");
  }

  [Fact]
  public async Task UpdateChangesNameAndCycleButIgnoresEmulator() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var created = await CreateQueueAsync(client).ConfigureAwait(true);
    var id = created.GetProperty("id").GetString();

    var resp = await client.PutAsJsonAsync(new Uri($"/api/queues/{id}", UriKind.Relative), new { name = "Farm2", cycleExecution = true, emulatorSerial = "emu-999" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var updated = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    updated.GetProperty("name").GetString().Should().Be("Farm2");
    updated.GetProperty("cycleExecution").GetBoolean().Should().BeTrue();
    updated.GetProperty("emulatorSerial").GetString().Should().Be("emu-1");
  }

  [Fact]
  public async Task DeleteRemovesQueue() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = (await CreateQueueAsync(client).ConfigureAwait(true)).GetProperty("id").GetString();

    (await client.DeleteAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task GetUnknownReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    (await client.GetAsync(new Uri("/api/queues/missing", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task MultipleQueuesMayBindToSameEmulator() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    await CreateQueueAsync(client, name: "A", serial: "emu-1").ConfigureAwait(true);
    await CreateQueueAsync(client, name: "B", serial: "emu-1").ConfigureAwait(true);

    var listResp = await client.GetAsync(new Uri("/api/queues", UriKind.Relative)).ConfigureAwait(true);
    var list = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    list.GetArrayLength().Should().Be(2);
  }
}
