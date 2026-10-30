using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// CA2000: the hand-built handle's token source lives as long as the test and is disposed in finally.
#pragma warning disable CA2000

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 171: <c>POST /api/queue-templates/{id}/upgrade</c>. The upgrade gives an entry ID to each entry that
/// has none. For a running queue that uses the template, it also gives the same IDs to the running entries and
/// moves the consumed schedule state to the new keys, with no change of any next due time.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueTemplateUpgradeEndpointTests {
  private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
  private static readonly string[] MatchingSequences = { "seq-a", "seq-b", "seq-c" };
  private static readonly string[] MismatchingSequences = { "seq-a", "seq-other", "seq-c" };
  private readonly string _dataDir;

  public QueueTemplateUpgradeEndpointTests() {
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

  private static Uri Upgrade(string id, string query = "") => new($"/api/queue-templates/{id}/upgrade{query}", UriKind.Relative);

  /// <summary>Writes an old template (no entry IDs): once-per-run, time-of-day timer, relative timer.</summary>
  private void WriteLegacyTemplate(string id) {
    var dir = Path.Combine(_dataDir, "queue-templates");
    Directory.CreateDirectory(dir);
    var json = "{\"Id\":\"" + id + "\",\"Name\":\"Legacy\",\"Entries\":[" +
      "{\"SequenceId\":\"seq-a\",\"ScheduleType\":\"OncePerRun\"}," +
      "{\"SequenceId\":\"seq-b\",\"ScheduleType\":\"Timer\",\"TimerTimeOfDay\":\"06:00:00\"}," +
      "{\"SequenceId\":\"seq-c\",\"ScheduleType\":\"Timer\",\"TimerRelativeOffset\":\"00:10:00\"}]}";
    File.WriteAllText(Path.Combine(dir, id + ".json"), json);
  }

  private static async Task<string> CreateLinkedQueueAsync(HttpClient client, string templateId) {
    var created = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Farm", emulatorSerial = "emu-offline", cycleExecution = true }).ConfigureAwait(true);
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var queueId = (await BodyAsync(created).ConfigureAwait(true)).GetProperty("id").GetString()!;
    var link = await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId }).ConfigureAwait(true);
    link.StatusCode.Should().Be(HttpStatusCode.OK);
    return queueId;
  }

  private static QueueRunSchedule BuildSchedule(QueueRunHandle handle) {
    var entries = new[] {
      new QueueTemplateEntry { SequenceId = "seq-a", ScheduleType = ScheduleType.OncePerRun },
      new QueueTemplateEntry { SequenceId = "seq-b", ScheduleType = ScheduleType.Timer, TimerTimeOfDay = new TimeOnly(6, 0) },
      new QueueTemplateEntry { SequenceId = "seq-c", ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromMinutes(10) },
    };
    handle.RunStartedAt = Noon.AddHours(-1);
    var schedule = new QueueRunSchedule(QueueRunPlan.Build(entries, 0), handle.RunStartedAt, cycling: true);
    handle.Schedule = schedule;
    return schedule;
  }

  private static async Task WithLiveRunAsync(
      WebApplicationFactory<Program> app, string queueId, QueueRunHandle handle, string[] runtimeSequences, Func<Task> body) {
    var registry = app.Services.GetRequiredService<IQueueRunRegistry>();
    var runtime = app.Services.GetRequiredService<IQueueRuntimeStore>();
    registry.TryAdd(queueId, handle).Should().BeTrue();
    runtime.ReplaceEntries(queueId, runtimeSequences.Select(s => new QueueEntry { SequenceId = s }));
    runtime.SetStatus(queueId, QueueExecutionStatus.Running);
    try {
      await body().ConfigureAwait(true);
    }
    finally {
      runtime.SetStatus(queueId, QueueExecutionStatus.Stopped);
      registry.Remove(queueId, out _);
      handle.Cts.Dispose();
    }
  }

  [Fact]
  public async Task UnknownTemplateGives404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.PostAsync(Upgrade("nope"), null).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task DryRunCountsTheEntriesAndChangesNothing() {
    WriteLegacyTemplate("legacy1");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.PostAsync(Upgrade("legacy1", "?dryRun=true"), null).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("entriesToChange").GetInt32().Should().Be(3);
    body.GetProperty("runningMatch").GetArrayLength().Should().Be(0);
    body.GetProperty("unmatched").GetArrayLength().Should().Be(0);
    var read = await BodyAsync(await client.GetAsync(new Uri("/api/queue-templates/legacy1", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
    read.GetProperty("needsUpgrade").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task UpgradeGivesIdsAndARepeatChangesNothing() {
    WriteLegacyTemplate("legacy2");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var first = await client.PostAsync(Upgrade("legacy2"), null).ConfigureAwait(true);
    first.StatusCode.Should().Be(HttpStatusCode.OK);
    (await BodyAsync(first).ConfigureAwait(true)).GetProperty("entriesChanged").GetInt32().Should().Be(3);

    var read = await BodyAsync(await client.GetAsync(new Uri("/api/queue-templates/legacy2", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
    read.GetProperty("needsUpgrade").GetBoolean().Should().BeFalse();
    var ids = read.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entryId").GetString()).ToList();
    ids.Should().OnlyContain(id => !string.IsNullOrEmpty(id));
    read.GetProperty("entries")[1].GetProperty("timerTimeOfDay").GetString().Should().Be("06:00");

    var second = await client.PostAsync(Upgrade("legacy2"), null).ConfigureAwait(true);
    (await BodyAsync(second).ConfigureAwait(true)).GetProperty("entriesChanged").GetInt32().Should().Be(0);
    var after = await BodyAsync(await client.GetAsync(new Uri("/api/queue-templates/legacy2", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
    after.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entryId").GetString()).Should().Equal(ids);
  }

  [Fact]
  public async Task ARunningQueueNeedsConfirmAndThenKeepsAllConsumedState() {
    WriteLegacyTemplate("legacy3");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var queueId = await CreateLinkedQueueAsync(client, "legacy3").ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = queueId, Cts = new CancellationTokenSource() };
    var schedule = BuildSchedule(handle);
    // Consumed state: the time-of-day timer fired today, the relative timer fired, the first entry ran in this cycle,
    // and a daily retry is armed for the time-of-day entry.
    var today = DateOnly.FromDateTime(Noon.DateTime);
    schedule.MarkTimeOfDayFired(1, today);
    schedule.MarkRelativeFired(2);
    schedule.BeginCycle();
    schedule.MarkOncePerRunCompleted(0);
    schedule.ArmDailyRetry(1, Noon.AddMinutes(30), attempt: 1);
    var dueBefore = schedule.ComputeNextDue(handle, Noon);

    await WithLiveRunAsync(app, queueId, handle, MatchingSequences, async () => {
      var withoutConfirm = await client.PostAsync(Upgrade("legacy3"), null).ConfigureAwait(true);
      withoutConfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
      (await BodyAsync(withoutConfirm).ConfigureAwait(true)).GetProperty("error").GetProperty("code").GetString().Should().Be("confirm_required");

      var preview = await BodyAsync(await client.PostAsync(Upgrade("legacy3", "?dryRun=true"), null).ConfigureAwait(true)).ConfigureAwait(true);
      preview.GetProperty("runningMatch").GetArrayLength().Should().Be(3);
      preview.GetProperty("unmatched").GetArrayLength().Should().Be(0);

      var done = await client.PostAsync(Upgrade("legacy3", "?confirm=true"), null).ConfigureAwait(true);
      done.StatusCode.Should().Be(HttpStatusCode.OK);

      var template = await BodyAsync(await client.GetAsync(new Uri("/api/queue-templates/legacy3", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
      var templateIds = template.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entryId").GetString()!).ToList();

      var plan = schedule.Plan;
      plan.Keys.Should().Equal(templateIds);
      plan.Entries.Select(e => e.EntryId).Should().Equal(templateIds);
      var runtime = app.Services.GetRequiredService<IQueueRuntimeStore>();
      runtime.GetEntries(queueId).Select(e => e.EntryId).Should().Equal(templateIds);

      // No consumed state is lost, and no next due time moves.
      schedule.TimeOfDayFiredOn(1, today).Should().BeTrue();
      schedule.RelativeFired(2).Should().BeTrue();
      schedule.OncePerRunCompletedThisCycle(0).Should().BeTrue();
      schedule.OncePerRunCompletedThisCycle(1).Should().BeFalse();
      schedule.DueDailyRetries(Noon.AddMinutes(31)).Should().Equal(new[] { (1, 1) });
      schedule.ComputeNextDue(handle, Noon).Should().Be(dueBefore);
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task ARunningEntryThatDoesNotMatchRefusesTheUpgradeAndChangesNothing() {
    WriteLegacyTemplate("legacy4");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var queueId = await CreateLinkedQueueAsync(client, "legacy4").ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = queueId, Cts = new CancellationTokenSource() };
    BuildSchedule(handle);

    await WithLiveRunAsync(app, queueId, handle, MismatchingSequences, async () => {
      var resp = await client.PostAsync(Upgrade("legacy4", "?confirm=true"), null).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
      var body = await BodyAsync(resp).ConfigureAwait(true);
      body.GetProperty("error").GetProperty("code").GetString().Should().Be("unmatched_entries");
      body.GetProperty("unmatched").EnumerateArray().Select(u => u.GetProperty("position").GetInt32()).Should().Contain(1);
      var template = await BodyAsync(await client.GetAsync(new Uri("/api/queue-templates/legacy4", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
      template.GetProperty("needsUpgrade").GetBoolean().Should().BeTrue();
    }).ConfigureAwait(true);
  }

  [Fact]
  public async Task ARunThatIsStartingRefusesTheUpgrade() {
    WriteLegacyTemplate("legacy5");
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var queueId = await CreateLinkedQueueAsync(client, "legacy5").ConfigureAwait(true);
    var handle = new QueueRunHandle { QueueId = queueId, Cts = new CancellationTokenSource() };

    await WithLiveRunAsync(app, queueId, handle, MatchingSequences, async () => {
      var resp = await client.PostAsync(Upgrade("legacy5", "?confirm=true"), null).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
      (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("error").GetProperty("code").GetString().Should().Be("unmatched_entries");
    }).ConfigureAwait(true);
  }
}
