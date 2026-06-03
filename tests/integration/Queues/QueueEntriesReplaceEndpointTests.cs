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
public sealed class QueueEntriesReplaceEndpointTests {
  private readonly string _dataDir;

  public QueueEntriesReplaceEndpointTests() {
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

  private static async Task<string> CreateQueueAsync(HttpClient client, bool cycle = false) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { name = "Farm", emulatorSerial = "emu-1", cycleExecution = cycle }).ConfigureAwait(true);
    var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    return json.GetProperty("id").GetString()!;
  }

  private static readonly string[] LoopSequenceIds = { "seq-loop" };

  private static async Task<string> CreateTemplateAsync(HttpClient client) {
    var entries = Array.ConvertAll(LoopSequenceIds, id => new { sequenceId = id });
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new { name = "Tpl-" + Guid.NewGuid().ToString("N"), entries, overwrite = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static Task<HttpResponseMessage> LinkTemplateAsync(HttpClient client, string queueId, string templateId) =>
    client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId });

  private static async Task<string> StartCyclingQueueAsync(HttpClient client) {
    var id = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    var tpl = await CreateTemplateAsync(client).ConfigureAwait(true);
    await LinkTemplateAsync(client, id, tpl).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    // Give the run a moment to start and stay Running
    await Task.Delay(100).ConfigureAwait(true);
    return id;
  }

  private static Task<HttpResponseMessage> ReplaceAsync(HttpClient client, string id, string[] sequenceIds) =>
    client.PutAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceIds });

  [Fact]
  public async Task ReplaceReturnsDetailWithResolvedNamesAndStale() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "old" }).ConfigureAwait(true);

    var ids = new[] { "seq-a", "ghost" };
    var resp = await ReplaceAsync(client, id, ids).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    var entries = root.GetProperty("entries");
    entries.GetArrayLength().Should().Be(2);
    entries[0].GetProperty("sequenceId").GetString().Should().Be("seq-a");
    entries[0].GetProperty("sequenceName").GetString().Should().Be("Alpha");
    entries[0].GetProperty("stale").GetBoolean().Should().BeFalse();
    entries[1].GetProperty("stale").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task ReplaceWithEmptyArrayClearsEntries() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "old" }).ConfigureAwait(true);

    var resp = await ReplaceAsync(client, id, Array.Empty<string>()).ConfigureAwait(true);
    var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    root.GetProperty("entries").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task ReplaceUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await ReplaceAsync(client, "nope", Array.Empty<string>()).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task ReplaceWhileRunningReturns409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await StartCyclingQueueAsync(client).ConfigureAwait(true);

    var ids = new[] { "seq-a" };
    var resp = await ReplaceAsync(client, id, ids).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    root.GetProperty("error").GetProperty("code").GetString().Should().Be("queue_running");

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }
}
