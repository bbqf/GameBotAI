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

/// <summary>
/// US2: PUT /api/queues/{id}/template sets or clears a queue's persisted linked template
/// (FR-002/003/004/005). The link references the template by stable ID.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueTemplateLinkEndpointTests {
  private readonly string _dataDir;

  public QueueTemplateLinkEndpointTests() {
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

  private static async Task<string> CreateQueueAsync(HttpClient client, string name = "Farm", bool cycle = false) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { name, emulatorSerial = "emu-1", cycleExecution = cycle }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> StartCyclingQueueAsync(HttpClient client, string queueId, string templateId) {
    await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId }).ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{queueId}/start", UriKind.Relative), null).ConfigureAwait(true);
    await Task.Delay(50).ConfigureAwait(true); // allow run to start
    return queueId;
  }

  private static async Task<string> CreateTemplateAsync(HttpClient client, string name, params string[] sequenceIds) {
    var entries = Array.ConvertAll(sequenceIds, id => new { sequenceId = id });
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new { name, entries, overwrite = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static Task<HttpResponseMessage> SetLinkAsync(HttpClient client, string queueId, string? templateId) =>
    client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId });

  private static async Task<string?> LinkedIdAsync(HttpClient client, string queueId) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative)).ConfigureAwait(true);
    var prop = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("linkedTemplateId");
    return prop.ValueKind == JsonValueKind.Null ? null : prop.GetString();
  }

  [Fact]
  public async Task SetLinkPersistsAcrossRestart() {
    SeedSequence("seq-a", "Alpha");
    string id, tplId;
    using (var app = new WebApplicationFactory<Program>()) {
      var client = NewClient(app);
      id = await CreateQueueAsync(client).ConfigureAwait(true);
      tplId = await CreateTemplateAsync(client, "T", "seq-a").ConfigureAwait(true);
      (await SetLinkAsync(client, id, tplId).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    using var app2 = new WebApplicationFactory<Program>();
    var client2 = NewClient(app2);
    (await LinkedIdAsync(client2, id).ConfigureAwait(true)).Should().Be(tplId);
  }

  [Fact]
  public async Task SetLinkToDifferentTemplateReplacesPrior() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var t1 = await CreateTemplateAsync(client, "T1", "seq-a").ConfigureAwait(true);
    var t2 = await CreateTemplateAsync(client, "T2", "seq-a").ConfigureAwait(true);

    await SetLinkAsync(client, id, t1).ConfigureAwait(true);
    await SetLinkAsync(client, id, t2).ConfigureAwait(true);

    (await LinkedIdAsync(client, id).ConfigureAwait(true)).Should().Be(t2);
  }

  [Fact]
  public async Task SetLinkNullClearsTheLink() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);
    var tplId = await CreateTemplateAsync(client, "T", "seq-a").ConfigureAwait(true);

    await SetLinkAsync(client, id, tplId).ConfigureAwait(true);
    (await SetLinkAsync(client, id, null).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.OK);

    (await LinkedIdAsync(client, id).ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task SetLinkToUnknownTemplateReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await SetLinkAsync(client, id, "ghost").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    root.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
  }

  [Fact]
  public async Task SetLinkOnUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    (await SetLinkAsync(client, "nope", null).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task SetLinkIsAllowedWhileRunning() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    // Use a different cycle template so the queue stays Running when SetLink fires
    var id = await CreateQueueAsync(client, cycle: true).ConfigureAwait(true);
    var cycleTemplateId = await CreateTemplateAsync(client, "CycleTpl", "seq-a").ConfigureAwait(true);
    await StartCyclingQueueAsync(client, id, cycleTemplateId).ConfigureAwait(true);

    var tplId = await CreateTemplateAsync(client, "T", "seq-a").ConfigureAwait(true);
    (await SetLinkAsync(client, id, tplId).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await LinkedIdAsync(client, id).ConfigureAwait(true)).Should().Be(tplId);

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact]
  public async Task SettingOneQueuesLinkLeavesOtherQueuesAndTemplateUnchanged() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var q1 = await CreateQueueAsync(client, "Q1").ConfigureAwait(true);
    var q2 = await CreateQueueAsync(client, "Q2").ConfigureAwait(true);
    var t1 = await CreateTemplateAsync(client, "T1", "seq-a").ConfigureAwait(true);
    var t2 = await CreateTemplateAsync(client, "T2", "seq-a").ConfigureAwait(true);
    await SetLinkAsync(client, q1, t1).ConfigureAwait(true);

    await SetLinkAsync(client, q2, t2).ConfigureAwait(true);

    (await LinkedIdAsync(client, q1).ConfigureAwait(true)).Should().Be(t1, "setting Q2's link must not change Q1's");
    // Template T1's contents remain intact (one entry).
    var t1Resp = await client.GetAsync(new Uri($"/api/queue-templates/{t1}", UriKind.Relative)).ConfigureAwait(true);
    JsonDocument.Parse(await t1Resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("entries").GetArrayLength().Should().Be(1);
  }
}
