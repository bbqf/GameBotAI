using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.QueueTemplates;

[Collection("ConfigIsolation")]
public sealed class QueueTemplatesReadEndpointTests {
  private readonly string _dataDir;

  public QueueTemplatesReadEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    _dataDir = TestEnvironment.PrepareCleanDataDir();
  }

  private void SeedSequence(string id, string name) {
    var dir = Path.Combine(_dataDir, "commands", "sequences");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, id + ".json"), $"{{\"Id\":\"{id}\",\"Name\":\"{name}\"}}");
  }

  private void SeedTemplate(string id, string name, params string[] sequenceIds) {
    var dir = Path.Combine(_dataDir, "queue-templates");
    Directory.CreateDirectory(dir);
    var entries = string.Join(",", Array.ConvertAll(sequenceIds, s => $"{{\"SequenceId\":\"{s}\"}}"));
    File.WriteAllText(Path.Combine(dir, id + ".json"),
      $"{{\"Id\":\"{id}\",\"Name\":\"{name}\",\"Entries\":[{entries}]}}");
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  [Fact]
  public async Task ListReturnsSummariesWithEntryCount() {
    SeedTemplate("tpl-1", "Daily Farm", "seq-a", "seq-b");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.GetAsync(new Uri("/api/queue-templates", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    root.GetArrayLength().Should().Be(1);
    root[0].GetProperty("name").GetString().Should().Be("Daily Farm");
    root[0].GetProperty("entryCount").GetInt32().Should().Be(2);
  }

  [Fact]
  public async Task DetailResolvesSequenceNamesAndStaleFlag() {
    SeedSequence("seq-a", "Alpha");
    SeedTemplate("tpl-2", "Mixed", "seq-a", "ghost");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.GetAsync(new Uri("/api/queue-templates/tpl-2", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    var entries = root.GetProperty("entries");
    entries.GetArrayLength().Should().Be(2);
    entries[0].GetProperty("sequenceId").GetString().Should().Be("seq-a");
    entries[0].GetProperty("sequenceName").GetString().Should().Be("Alpha");
    entries[0].GetProperty("stale").GetBoolean().Should().BeFalse();
    entries[1].GetProperty("stale").GetBoolean().Should().BeTrue();
    entries[1].GetProperty("sequenceName").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task DetailForUnknownIdReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.GetAsync(new Uri("/api/queue-templates/nope", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }
}
