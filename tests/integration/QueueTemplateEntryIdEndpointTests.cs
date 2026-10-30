using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 171: the entry ID rules of a template save. A new template gets IDs. An overwrite of an upgraded
/// template keeps a supplied ID and creates one for a new entry. An overwrite of an old template creates no
/// ID (no silent upgrade). A duplicate ID gives 400.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueTemplateEntryIdEndpointTests {
  private readonly string _dataDir;

  public QueueTemplateEntryIdEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    _dataDir = TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<JsonElement> BodyAsync(HttpResponseMessage resp) =>
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();

  private static async Task<JsonElement> GetTemplateAsync(HttpClient client, string id) {
    var read = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    read.StatusCode.Should().Be(HttpStatusCode.OK);
    return await BodyAsync(read).ConfigureAwait(true);
  }

  private static bool HasEntryId(JsonElement entry) => entry.TryGetProperty("entryId", out _);

  private string WriteLegacyTemplate(string id) {
    var dir = Path.Combine(_dataDir, "queue-templates");
    Directory.CreateDirectory(dir);
    var json = "{\"Id\":\"" + id + "\",\"Name\":\"Legacy Template\",\"Entries\":[" +
      "{\"SequenceId\":\"seq-a\",\"ScheduleType\":\"OncePerRun\"}," +
      "{\"SequenceId\":\"seq-a\",\"ScheduleType\":\"EveryStep\"}]}";
    File.WriteAllText(Path.Combine(dir, id + ".json"), json);
    return id;
  }

  [Fact]
  public async Task NewTemplateGetsAnIdForEachEntry() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var entries = new object[] {
      new { sequenceId = "seq-a", scheduleType = "OncePerRun" },
      new { sequenceId = "seq-a", scheduleType = "OncePerRun" },
    };

    var save = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Fresh", entries, overwrite = false }).ConfigureAwait(true);
    save.StatusCode.Should().Be(HttpStatusCode.Created);
    var saved = await BodyAsync(save).ConfigureAwait(true);

    saved.GetProperty("needsUpgrade").GetBoolean().Should().BeFalse();
    var ids = saved.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entryId").GetString()).ToList();
    ids.Should().OnlyContain(id => !string.IsNullOrEmpty(id));
    ids.Distinct().Should().HaveCount(2);
  }

  [Fact]
  public async Task OverwriteOfAnUpgradedTemplateKeepsASuppliedIdAndCreatesOneForANewEntry() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var first = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Keep", entries = new object[] { new { sequenceId = "seq-a" } }, overwrite = false }).ConfigureAwait(true);
    var firstBody = await BodyAsync(first).ConfigureAwait(true);
    var keptId = firstBody.GetProperty("entries")[0].GetProperty("entryId").GetString();

    var second = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new {
        name = "Keep",
        overwrite = true,
        entries = new object[] {
          new { sequenceId = "seq-b" },
          new { entryId = keptId, sequenceId = "seq-a" },
        }
      }).ConfigureAwait(true);
    second.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await BodyAsync(second).ConfigureAwait(true);

    var entries = body.GetProperty("entries");
    entries[1].GetProperty("entryId").GetString().Should().Be(keptId);
    var newId = entries[0].GetProperty("entryId").GetString();
    newId.Should().NotBeNullOrEmpty();
    newId.Should().NotBe(keptId);
  }

  [Fact]
  public async Task OverwriteOfAnOldTemplateCreatesNoId() {
    var legacyId = WriteLegacyTemplate("legacy171");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var before = await GetTemplateAsync(client, legacyId).ConfigureAwait(true);
    before.GetProperty("needsUpgrade").GetBoolean().Should().BeTrue();

    var save = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new {
        name = "Legacy Template",
        overwrite = true,
        entries = new object[] { new { sequenceId = "seq-a" }, new { sequenceId = "seq-b" } }
      }).ConfigureAwait(true);
    save.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await BodyAsync(save).ConfigureAwait(true);

    body.GetProperty("needsUpgrade").GetBoolean().Should().BeTrue();
    body.GetProperty("entries").EnumerateArray().Any(HasEntryId).Should().BeFalse();
  }

  [Fact]
  public async Task ReadOfAnOldTemplateShowsNeedsUpgradeAndNoIds() {
    var legacyId = WriteLegacyTemplate("legacy172");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var body = await GetTemplateAsync(client, legacyId).ConfigureAwait(true);

    body.GetProperty("needsUpgrade").GetBoolean().Should().BeTrue();
    body.GetProperty("entries").EnumerateArray().Any(HasEntryId).Should().BeFalse();

    var list = await client.GetAsync(new Uri("/api/queue-templates", UriKind.Relative)).ConfigureAwait(true);
    var listBody = await BodyAsync(list).ConfigureAwait(true);
    listBody.EnumerateArray()
      .Single(t => t.GetProperty("id").GetString() == legacyId)
      .GetProperty("needsUpgrade").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task DuplicateIdInOneTemplateGives400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var save = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new {
        name = "Dup",
        overwrite = false,
        entries = new object[] {
          new { entryId = "same", sequenceId = "seq-a" },
          new { entryId = "SAME", sequenceId = "seq-b" },
        }
      }).ConfigureAwait(true);

    save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await BodyAsync(save).ConfigureAwait(true);
    body.GetProperty("error").GetProperty("message").GetString().Should().Contain("entryId");
  }
}
