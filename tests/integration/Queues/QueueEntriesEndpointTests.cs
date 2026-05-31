using System;
using System.IO;
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
public sealed class QueueEntriesEndpointTests {
  private readonly string _dataDir;

  public QueueEntriesEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    _dataDir = TestEnvironment.PrepareCleanDataDir();
  }

  private void SeedSequence(string id, string name) {
    var dir = Path.Combine(_dataDir, "commands", "sequences");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, id + ".json"), $"{{\"Id\":\"{id}\",\"Name\":\"{name}\"}}");
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateQueueAsync(HttpClient client) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { name = "Farm", emulatorSerial = "emu-1" }).ConfigureAwait(true);
    var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    return json.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetDetailAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  [Fact]
  public async Task AddEntryAppendsAndDetailPreservesOrder() {
    SeedSequence("seq-a", "Alpha");
    SeedSequence("seq-b", "Bravo");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "seq-a" }).ConfigureAwait(true);
    await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "seq-b" }).ConfigureAwait(true);

    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);
    var entries = detail.GetProperty("entries");
    entries.GetArrayLength().Should().Be(2);
    entries[0].GetProperty("sequenceId").GetString().Should().Be("seq-a");
    entries[0].GetProperty("sequenceName").GetString().Should().Be("Alpha");
    entries[0].GetProperty("stale").GetBoolean().Should().BeFalse();
    entries[1].GetProperty("sequenceId").GetString().Should().Be("seq-b");
    detail.GetProperty("entryCount").GetInt32().Should().Be(2);
  }

  [Fact]
  public async Task EntryForDeletedSequenceIsFlaggedStale() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "ghost" }).ConfigureAwait(true);

    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);
    var entry = detail.GetProperty("entries")[0];
    entry.GetProperty("stale").GetBoolean().Should().BeTrue();
    entry.GetProperty("sequenceName").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task AddEntryWithoutSequenceIdReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task RemoveEntrySucceedsAndUnknownReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var addResp = await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "ghost" }).ConfigureAwait(true);
    var entryId = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("entryId").GetString();

    (await client.DeleteAsync(new Uri($"/api/queues/{id}/entries/{entryId}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.DeleteAsync(new Uri($"/api/queues/{id}/entries/{entryId}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task EntriesAreEmptyForFreshlyCreatedQueue() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);
    detail.GetProperty("entries").GetArrayLength().Should().Be(0);
  }
}
