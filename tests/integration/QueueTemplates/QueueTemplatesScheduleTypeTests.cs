using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

  // ── feature 059: relative-offset timer round-trip + validation ─────────────

  [Fact]
  public async Task SaveTimerRelativeOffsetRoundTripsScheduleTypeAndOffset() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "Timer", timerRelativeOffset = "00:10:00" } };
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "RelativeTemplate", entries, overwrite = false }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await BodyAsync(createResp).ConfigureAwait(true);
    var id = created.GetProperty("id").GetString()!;

    var getResp = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    var body = await BodyAsync(getResp).ConfigureAwait(true);
    var entry = body.GetProperty("entries")[0];
    entry.GetProperty("scheduleType").GetString().Should().Be("Timer");
    entry.GetProperty("timerRelativeOffset").GetString().Should().Be("00:10:00");
    entry.GetProperty("timerTimeOfDay").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task SaveTimerRelativeOffsetZeroIsValid() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "Timer", timerRelativeOffset = "00:00:00" } };
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "ZeroOffset", entries, overwrite = false }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await BodyAsync(createResp).ConfigureAwait(true);
    var id = created.GetProperty("id").GetString()!;

    var getResp = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    var body = await BodyAsync(getResp).ConfigureAwait(true);
    body.GetProperty("entries")[0].GetProperty("timerRelativeOffset").GetString().Should().Be("00:00:00");
  }

  [Fact]
  public async Task TimerWithBothTimerFieldsReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "Timer", timerTimeOfDay = "15:30", timerRelativeOffset = "00:10:00" } };
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Both", entries, overwrite = false }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    body.GetProperty("error").GetProperty("message").GetString().Should().Contain("exactly one");
  }

  [Fact]
  public async Task TimerWithNegativeRelativeOffsetReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "Timer", timerRelativeOffset = "-00:10:00" } };
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Negative", entries, overwrite = false }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    body.GetProperty("error").GetProperty("message").GetString().Should().Contain("timerRelativeOffset");
  }

  // ── feature 060 (T005): at-queue-start round-trip + end-to-end ordering ────

  [Fact]
  public async Task SaveAtQueueStartEntryRoundTripsCorrectly() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var entries = new[] { new { sequenceId = "seq-a", scheduleType = "AtQueueStart" } };
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "AtStartTemplate", entries, overwrite = false }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await BodyAsync(createResp).ConfigureAwait(true);
    var id = created.GetProperty("id").GetString()!;

    var getResp = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    var body = await BodyAsync(getResp).ConfigureAwait(true);
    var entry = body.GetProperty("entries")[0];
    entry.GetProperty("scheduleType").GetString().Should().Be("AtQueueStart");
    entry.GetProperty("timerTimeOfDay").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact] // SC-001: at-queue-start entries execute before timers and normal steps in a real run
  public async Task AtQueueStartExecutesBeforeTimersAndNormalStepsInRealRun() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    // Three distinct real sequences so each produces its own execution-log entry.
    var startSeq = await CreateSequenceAsync(client, "AtStart").ConfigureAwait(true);
    var timerSeq = await CreateSequenceAsync(client, "Timer").ConfigureAwait(true);
    var stepSeq = await CreateSequenceAsync(client, "Step").ConfigureAwait(true);

    // Template order deliberately lists the at-queue-start entry LAST and the timer is past-due
    // (00:00), so only the run-start pre-pass can make the at-queue-start sequence fire first.
    var entries = new object[] {
      new { sequenceId = timerSeq, scheduleType = "Timer", timerTimeOfDay = "00:00" },
      new { sequenceId = stepSeq, scheduleType = "OncePerRun" },
      new { sequenceId = startSeq, scheduleType = "AtQueueStart" },
    };
    var tplResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "OrderingTemplate", entries, overwrite = false }).ConfigureAwait(true);
    tplResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var templateId = (await BodyAsync(tplResp).ConfigureAwait(true)).GetProperty("id").GetString()!;

    // Non-cycling queue so the run executes one pass and settles back to Stopped.
    var queueResp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "OrderingQueue", emulatorSerial = "emu-offline", cycleExecution = false }).ConfigureAwait(true);
    var queueId = (await BodyAsync(queueResp).ConfigureAwait(true)).GetProperty("id").GetString()!;
    await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative),
      new { templateId }).ConfigureAwait(true);

    await client.PostAsync(new Uri($"/api/queues/{queueId}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForStatusAsync(client, queueId, "Stopped").ConfigureAwait(true);

    // Read back the per-sequence execution-log index assigned during the run; the at-queue-start
    // sequence must have fired before both the timer and the once-per-run sequence.
    var log = app.Services.GetRequiredService<IExecutionLogService>();
    var startIdx = await SequenceIndexAsync(log, startSeq).ConfigureAwait(true);
    var timerIdx = await SequenceIndexAsync(log, timerSeq).ConfigureAwait(true);
    var stepIdx = await SequenceIndexAsync(log, stepSeq).ConfigureAwait(true);

    startIdx.Should().BeLessThan(timerIdx, "at-queue-start runs before timer evaluation");
    startIdx.Should().BeLessThan(stepIdx, "at-queue-start runs before the first OncePerRun step");
  }

  private static async Task<string> CreateSequenceAsync(HttpClient client, string namePrefix) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative),
      new { name = namePrefix + " " + Guid.NewGuid().ToString("N"), steps = Array.Empty<string>() }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("id").GetString()!;
  }

  private static async Task<int> SequenceIndexAsync(IExecutionLogService log, string sequenceId) {
    var page = await log.QueryAsync(new ExecutionLogQuery {
      ObjectType = "sequence", ObjectId = sequenceId, PageSize = 10
    }).ConfigureAwait(true);
    var entry = page.Items.Single();
    return entry.Hierarchy.SequenceIndex ?? int.MaxValue;
  }

  private static async Task<string> StatusAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
    return (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("status").GetString()!;
  }

  private static async Task WaitForStatusAsync(HttpClient client, string id, string expected, int timeoutMs = 5000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if (await StatusAsync(client, id).ConfigureAwait(true) == expected) return;
      await Task.Delay(25).ConfigureAwait(true);
    }
  }
}
