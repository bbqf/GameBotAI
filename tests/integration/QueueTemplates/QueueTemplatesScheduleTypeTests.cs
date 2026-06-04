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

namespace GameBot.IntegrationTests.QueueTemplates;

[Collection("ConfigIsolation")]
public sealed class QueueTemplatesScheduleTypeTests {
  private readonly string _dataDir;

  public QueueTemplatesScheduleTypeTests() {
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

  // ── T025: round-trip persistence ─────────────────────────────────────────

  [Fact]
  public async Task SaveEveryStepEntryRoundTripsCorrectly() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "EveryStep" } };
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "TestSchedule", entries, overwrite = false }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await BodyAsync(createResp).ConfigureAwait(true);
    var id = created.GetProperty("id").GetString()!;

    var getResp = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    var body = await BodyAsync(getResp).ConfigureAwait(true);
    var entry = body.GetProperty("entries")[0];
    entry.GetProperty("scheduleType").GetString().Should().Be("EveryStep");
    entry.GetProperty("timerTimeOfDay").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task SaveTimerEntryRoundTripsScheduleTypeAndTime() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "Timer", timerTimeOfDay = "15:30" } };
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "TimerTemplate", entries, overwrite = false }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await BodyAsync(createResp).ConfigureAwait(true);
    var id = created.GetProperty("id").GetString()!;

    var getResp = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    var body = await BodyAsync(getResp).ConfigureAwait(true);
    var entry = body.GetProperty("entries")[0];
    entry.GetProperty("scheduleType").GetString().Should().Be("Timer");
    entry.GetProperty("timerTimeOfDay").GetString().Should().Be("15:30");
  }

  [Fact]
  public async Task OmittedScheduleTypeDefaultsToOncePerRun() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a" } };
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "DefaultSchedule", entries, overwrite = false }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await BodyAsync(createResp).ConfigureAwait(true);
    var id = created.GetProperty("id").GetString()!;

    var getResp = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    var body = await BodyAsync(getResp).ConfigureAwait(true);
    var entry = body.GetProperty("entries")[0];
    entry.GetProperty("scheduleType").GetString().Should().Be("OncePerRun");
    entry.GetProperty("timerTimeOfDay").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task PreFeatureTemplateFileReturnsOncePerRunForAllEntries() {
    // Template JSON written before schedule types were introduced (no ScheduleType field)
    var dir = Path.Combine(_dataDir, "queue-templates");
    Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir, "legacy.json"),
      """{"Id":"legacy","Name":"Legacy","Entries":[{"SequenceId":"seq-a"},{"SequenceId":"seq-b"}]}""").ConfigureAwait(true);

    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var getResp = await client.GetAsync(new Uri("/api/queue-templates/legacy", UriKind.Relative)).ConfigureAwait(true);
    getResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await BodyAsync(getResp).ConfigureAwait(true);
    var entries = body.GetProperty("entries");
    entries.GetArrayLength().Should().Be(2);
    entries[0].GetProperty("scheduleType").GetString().Should().Be("OncePerRun");
    entries[1].GetProperty("scheduleType").GetString().Should().Be("OncePerRun");
    entries[0].GetProperty("timerTimeOfDay").ValueKind.Should().Be(JsonValueKind.Null);
  }

  // ── T026–T028: API validation ─────────────────────────────────────────────

  [Fact]
  public async Task TimerEntryMissingTimerTimeOfDayReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "Timer" } };
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Bad", entries, overwrite = false }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    body.GetProperty("error").GetProperty("message").GetString().Should()
      .Contain("timerTimeOfDay");
  }

  [Fact]
  public async Task InvalidScheduleTypeValueReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "Weekly" } };
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Bad", entries, overwrite = false }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    body.GetProperty("error").GetProperty("message").GetString().Should()
      .Contain("Weekly");
  }

  [Fact]
  public async Task BlankSequenceIdInEntryReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "   " } };
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Bad", entries, overwrite = false }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    body.GetProperty("error").GetProperty("message").GetString().Should()
      .Contain("sequenceId");
  }

  // ── T029: backward compatibility (duplicate of T025 pre-feature test, kept as US4 explicit) ──

  [Fact]
  public async Task InvalidTimerTimeFormatReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "Timer", timerTimeOfDay = "5pm" } };
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Bad", entries, overwrite = false }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    body.GetProperty("error").GetProperty("message").GetString().Should()
      .Contain("timerTimeOfDay");
  }
}
