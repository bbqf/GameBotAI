using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// US1: a queue linked to a template auto-loads that template's entries into the queue's
/// runtime on the first display after a service start (FR-006/006a/008/009/010/011/012).
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueAutoLoadEndpointTests {
  private readonly string _dataDir;

  public QueueAutoLoadEndpointTests() {
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
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateTemplateAsync(HttpClient client, params string[] sequenceIds) {
    var entries = Array.ConvertAll(sequenceIds, id => new { sequenceId = id });
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Daily Farm", entries, overwrite = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static Task<HttpResponseMessage> SetLinkAsync(HttpClient client, string queueId, string? templateId) =>
    client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId });

  private static async Task<JsonElement> GetDetailAsync(HttpClient client, string queueId) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative)).ConfigureAwait(true);
    resp.EnsureSuccessStatusCode();
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
  }

  [Fact]
  public async Task LinkedAndFreshMaterializesTemplateEntries() {
    SeedSequence("seq-a", "Alpha");
    SeedSequence("seq-b", "Beta");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var tplId = await CreateTemplateAsync(client, "seq-a", "seq-b").ConfigureAwait(true);
    await SetLinkAsync(client, id, tplId).ConfigureAwait(true);

    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);

    var entries = detail.GetProperty("entries");
    entries.GetArrayLength().Should().Be(2);
    entries[0].GetProperty("sequenceId").GetString().Should().Be("seq-a");
    entries[1].GetProperty("sequenceId").GetString().Should().Be("seq-b");
    detail.GetProperty("linkedTemplateId").GetString().Should().Be(tplId);
    detail.GetProperty("linkedTemplateName").GetString().Should().Be("Daily Farm");
  }

  [Fact]
  public async Task UnlinkedQueueIsNotAutoLoadedAndHasNoError() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);

    detail.GetProperty("entries").GetArrayLength().Should().Be(0);
    detail.GetProperty("linkedTemplateId").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task RunningQueueIsNotAutoLoaded() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    // Use a cycle-execution queue so the run stays Running long enough to assert on
    var id = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    var tplId = await CreateTemplateAsync(client, "seq-a").ConfigureAwait(true);
    await SetLinkAsync(client, id, tplId).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await Task.Delay(50).ConfigureAwait(true); // ensure run is in flight

    // A running queue skips auto-load (FR-010); entries come from the run, not the GET trigger
    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);

    // The run has materialized entries from the template, but auto-load did not fire separately
    detail.GetProperty("status").GetString().Should().Be("Running");

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact]
  public async Task AlreadyMaterializedQueueIsNotRefilledAfterDeliberateClear() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var tplId = await CreateTemplateAsync(client, "seq-a").ConfigureAwait(true);
    await SetLinkAsync(client, id, tplId).ConfigureAwait(true);

    // First display materializes; then operator deliberately clears the entries.
    await GetDetailAsync(client, id).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceIds = Array.Empty<string>() }).ConfigureAwait(true);

    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);

    detail.GetProperty("entries").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task MissingTemplateOnFreshDisplayClearsLinkAndOpensEmpty() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var tplId = await CreateTemplateAsync(client, "seq-a").ConfigureAwait(true);
    await SetLinkAsync(client, id, tplId).ConfigureAwait(true);
    await client.DeleteAsync(new Uri($"/api/queue-templates/{tplId}", UriKind.Relative)).ConfigureAwait(true);

    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);

    detail.GetProperty("entries").GetArrayLength().Should().Be(0);
    detail.GetProperty("linkedTemplateId").ValueKind.Should().Be(JsonValueKind.Null);
    detail.GetProperty("linkedTemplateName").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task DeletedTemplateLeavesAlreadyMaterializedEntriesUnchanged() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var tplId = await CreateTemplateAsync(client, "seq-a").ConfigureAwait(true);
    await SetLinkAsync(client, id, tplId).ConfigureAwait(true);

    await GetDetailAsync(client, id).ConfigureAwait(true); // materialize
    await client.DeleteAsync(new Uri($"/api/queue-templates/{tplId}", UriKind.Relative)).ConfigureAwait(true);

    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);

    // Guard skips on existing runtime state: entries kept (FR-014), link not cleared mid-session.
    detail.GetProperty("entries").GetArrayLength().Should().Be(1);
    detail.GetProperty("linkedTemplateId").GetString().Should().Be(tplId);
  }

  [Fact]
  public async Task AutoLoadSurvivesRestartAndIsRunnable() {
    SeedSequence("seq-a", "Alpha");
    var tplId = string.Empty;
    string id;
    using (var app = new WebApplicationFactory<Program>()) {
      var client = NewClient(app);
      id = await CreateQueueAsync(client).ConfigureAwait(true);
      tplId = await CreateTemplateAsync(client, "seq-a").ConfigureAwait(true);
      await SetLinkAsync(client, id, tplId).ConfigureAwait(true);
    }

    // "Restart": fresh app instance (new runtime store) against the same persisted data dir.
    using var app2 = new WebApplicationFactory<Program>();
    var client2 = NewClient(app2);
    var detail = await GetDetailAsync(client2, id).ConfigureAwait(true);
    detail.GetProperty("entries").GetArrayLength().Should().Be(1);

    var startResp = await client2.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    JsonDocument.Parse(await startResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("entryCount").GetInt32().Should().Be(1);
  }

  [Fact]
  public async Task AutoLoadOfLargeTemplateIsWithinBudget() {
    for (var i = 0; i < 100; i++) SeedSequence($"seq-{i}", $"S{i}");
    var entries = new object[100];
    for (var i = 0; i < 100; i++) entries[i] = new { sequenceId = $"seq-{i}" };

    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var tplResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Big", entries, overwrite = false }).ConfigureAwait(true);
    var tplId = JsonDocument.Parse(await tplResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
    await SetLinkAsync(client, id, tplId).ConfigureAwait(true);

    var sw = Stopwatch.StartNew();
    var detail = await GetDetailAsync(client, id).ConfigureAwait(true);
    sw.Stop();

    detail.GetProperty("entries").GetArrayLength().Should().Be(100);
    sw.ElapsedMilliseconds.Should().BeLessThan(1000);
  }
}
