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
/// Feature 083: POST /api/queues/{id}/duplicate creates a 1:1 copy of the source queue's
/// configuration and currently loaded entries under a new, required-to-differ name, always
/// created stopped, never mutating the source (FR-001..FR-008, FR-003a). The emulator fields
/// (serial/instance name/instance index) are the one exception: the caller resubmits them and MAY
/// change them, but — unlike the name — they are also allowed to come back unchanged (FR-003b).
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueuesDuplicateEndpointTests {
  private readonly string _dataDir;

  public QueuesDuplicateEndpointTests() {
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

  private static async Task<JsonElement> CreateQueueAsync(
      HttpClient client, string name = "Farm", string serial = "emu-1", bool cycle = false,
      bool pauseWhenIdle = false, int idleThresholdSeconds = 0,
      string? emulatorInstanceName = null, int? emulatorInstanceIndex = null) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new {
      name, emulatorSerial = serial, cycleExecution = cycle, pauseWhenIdle, idleThresholdSeconds,
      emulatorInstanceName, emulatorInstanceIndex
    }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  private static async Task<string> CreateTemplateAsync(HttpClient client, string name, params string[] sequenceIds) {
    var entries = Array.ConvertAll(sequenceIds, id => new { sequenceId = id });
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new { name, entries, overwrite = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateGameAsync(HttpClient client, string name) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name, description = "" }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static Task<HttpResponseMessage> AddEntryAsync(HttpClient client, string queueId, string sequenceId) =>
    client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/entries", UriKind.Relative), new { sequenceId });

  private static Task<HttpResponseMessage> DuplicateAsync(
      HttpClient client, string queueId, string? name, string? emulatorSerial = "emu-1",
      string? emulatorInstanceName = null, int? emulatorInstanceIndex = null) =>
    client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/duplicate", UriKind.Relative),
      new { name, emulatorSerial, emulatorInstanceName, emulatorInstanceIndex });

  private static async Task<JsonElement> GetQueueAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  [Fact]
  public async Task DuplicateCopiesConfigTemplateGameAndEntriesUnderNewNameAndId() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var source = await CreateQueueAsync(client, name: "Daily Farming", serial: "emu-1", cycle: true,
        pauseWhenIdle: true, idleThresholdSeconds: 45, emulatorInstanceName: "PNS", emulatorInstanceIndex: 2).ConfigureAwait(true);
    var sourceId = source.GetProperty("id").GetString()!;
    var templateId = await CreateTemplateAsync(client, "Farm Template", "seq-a").ConfigureAwait(true);
    var gameId = await CreateGameAsync(client, "Some Game").ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{sourceId}/template", UriKind.Relative), new { templateId }).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{sourceId}/game", UriKind.Relative), new { gameId }).ConfigureAwait(true);
    await AddEntryAsync(client, sourceId, "seq-a").ConfigureAwait(true);

    var resp = await DuplicateAsync(client, sourceId, "Daily Farming 2",
        emulatorSerial: "emu-1", emulatorInstanceName: "PNS", emulatorInstanceIndex: 2).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    resp.Headers.Location!.ToString().Should().StartWith("/api/queues/");
    var dup = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    dup.GetProperty("name").GetString().Should().Be("Daily Farming 2");
    dup.GetProperty("id").GetString().Should().NotBe(sourceId);
    dup.GetProperty("emulatorSerial").GetString().Should().Be("emu-1");
    dup.GetProperty("cycleExecution").GetBoolean().Should().BeTrue();
    dup.GetProperty("pauseWhenIdle").GetBoolean().Should().BeTrue();
    dup.GetProperty("idleThresholdSeconds").GetInt32().Should().Be(45);
    dup.GetProperty("emulatorInstanceName").GetString().Should().Be("PNS");
    dup.GetProperty("emulatorInstanceIndex").GetInt32().Should().Be(2);
    dup.GetProperty("linkedTemplateId").GetString().Should().Be(templateId);
    dup.GetProperty("linkedGameId").GetString().Should().Be(gameId);
    dup.GetProperty("status").GetString().Should().Be("Stopped");
    dup.GetProperty("entryCount").GetInt32().Should().Be(1);
  }

  [Fact]
  public async Task DuplicateOfQueueWithNoTemplateOrGameCopiesTheAbsence() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var source = await CreateQueueAsync(client, name: "Bare").ConfigureAwait(true);
    var sourceId = source.GetProperty("id").GetString()!;

    var resp = await DuplicateAsync(client, sourceId, "Bare 2").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var dup = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    dup.GetProperty("linkedTemplateId").ValueKind.Should().Be(JsonValueKind.Null);
    dup.GetProperty("linkedGameId").ValueKind.Should().Be(JsonValueKind.Null);
    dup.GetProperty("entryCount").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task DuplicateWhileSourceRunningStillSucceedsAndNewQueueIsStopped() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var source = await CreateQueueAsync(client, name: "Running Source", cycle: true).ConfigureAwait(true);
    var sourceId = source.GetProperty("id").GetString()!;
    var templateId = await CreateTemplateAsync(client, "T", "seq-a").ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{sourceId}/template", UriKind.Relative), new { templateId }).ConfigureAwait(true);
    (await client.PostAsync(new Uri($"/api/queues/{sourceId}/start", UriKind.Relative), null).ConfigureAwait(true))
      .StatusCode.Should().Be(HttpStatusCode.OK);

    var resp = await DuplicateAsync(client, sourceId, "Running Source 2").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var dup = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    dup.GetProperty("status").GetString().Should().Be("Stopped");

    await client.PostAsync(new Uri($"/api/queues/{sourceId}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact]
  public async Task DuplicateWithBlankNameReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var sourceId = (await CreateQueueAsync(client).ConfigureAwait(true)).GetProperty("id").GetString()!;

    var resp = await DuplicateAsync(client, sourceId, "   ").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Should().Contain("name is required");
  }

  [Fact]
  public async Task DuplicateWithUnchangedNameReturns400AndCreatesNoNewQueue() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var sourceId = (await CreateQueueAsync(client, name: "Same Name").ConfigureAwait(true)).GetProperty("id").GetString()!;

    var resp = await DuplicateAsync(client, sourceId, "Same Name").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Should().Contain("must differ");

    var listResp = await client.GetAsync(new Uri("/api/queues", UriKind.Relative)).ConfigureAwait(true);
    JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetArrayLength().Should().Be(1);
  }

  [Fact]
  public async Task DuplicateOfUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    (await DuplicateAsync(client, "missing", "Anything").ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task DuplicateLeavesSourceQueueCompletelyUnchanged() {
    SeedSequence("seq-a", "Alpha");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var source = await CreateQueueAsync(client, name: "Original", cycle: true, pauseWhenIdle: true, idleThresholdSeconds: 60).ConfigureAwait(true);
    var sourceId = source.GetProperty("id").GetString()!;
    var templateId = await CreateTemplateAsync(client, "T", "seq-a").ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{sourceId}/template", UriKind.Relative), new { templateId }).ConfigureAwait(true);
    var before = await GetQueueAsync(client, sourceId).ConfigureAwait(true);

    // Duplicating onto a different emulator must not retarget the source's own emulator either.
    await DuplicateAsync(client, sourceId, "A Copy", emulatorSerial: "emu-2").ConfigureAwait(true);

    var after = await GetQueueAsync(client, sourceId).ConfigureAwait(true);
    after.GetProperty("name").GetString().Should().Be(before.GetProperty("name").GetString());
    after.GetProperty("emulatorSerial").GetString().Should().Be(before.GetProperty("emulatorSerial").GetString());
    after.GetProperty("linkedTemplateId").GetString().Should().Be(before.GetProperty("linkedTemplateId").GetString());
    after.GetProperty("entryCount").GetInt32().Should().Be(before.GetProperty("entryCount").GetInt32());
    after.GetProperty("status").GetString().Should().Be(before.GetProperty("status").GetString());
  }

  [Fact]
  public async Task DuplicateWithDifferentEmulatorUsesTheRequestedEmulatorNotTheSource() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var source = await CreateQueueAsync(client, name: "Original", serial: "emu-1",
        emulatorInstanceName: "PNS", emulatorInstanceIndex: 2).ConfigureAwait(true);
    var sourceId = source.GetProperty("id").GetString()!;

    var resp = await DuplicateAsync(client, sourceId, "On Another Emulator",
        emulatorSerial: "emu-2", emulatorInstanceName: "Other", emulatorInstanceIndex: 9).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var dup = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    dup.GetProperty("emulatorSerial").GetString().Should().Be("emu-2");
    dup.GetProperty("emulatorInstanceName").GetString().Should().Be("Other");
    dup.GetProperty("emulatorInstanceIndex").GetInt32().Should().Be(9);

    var sourceAfter = await GetQueueAsync(client, sourceId).ConfigureAwait(true);
    sourceAfter.GetProperty("emulatorSerial").GetString().Should().Be("emu-1");
  }

  [Fact]
  public async Task DuplicateWithSameEmulatorAsSourceStillSucceedsUnlikeName() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var sourceId = (await CreateQueueAsync(client, name: "Original", serial: "emu-1").ConfigureAwait(true))
      .GetProperty("id").GetString()!;

    var resp = await DuplicateAsync(client, sourceId, "A Copy", emulatorSerial: "emu-1").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var dup = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    dup.GetProperty("emulatorSerial").GetString().Should().Be("emu-1");
  }

  [Fact]
  public async Task DuplicateWithMissingEmulatorSerialReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var sourceId = (await CreateQueueAsync(client).ConfigureAwait(true)).GetProperty("id").GetString()!;

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{sourceId}/duplicate", UriKind.Relative),
      new { name = "A Copy" }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).Should().Contain("emulatorSerial is required");
  }
}
