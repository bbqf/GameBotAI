using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// CA2000: the hand-built handle's token source lives as long as the test and is disposed in finally.
#pragma warning disable CA2000, CA1861

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 171: <c>GET /api/queues/{id}/template-diff</c> and <c>POST /api/queues/{id}/apply-template</c>. A real run loop
/// needs a device, so these tests register a hand-built run handle with the plan of the linked template and move its
/// pause register the way the run loop does. They use the real template and sequence stores.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueApplyTemplateEndpointTests {
  private static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
  private readonly string _dataDir;

  public QueueApplyTemplateEndpointTests() {
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

  private sealed class Live {
    public required string QueueId { get; init; }
    public required string TemplateId { get; init; }
    public required QueueRunHandle Handle { get; init; }
    public required List<string> EntryIds { get; init; }
  }

  private static async Task CreateSequencesAsync(WebApplicationFactory<Program> app, params string[] ids) {
    var repo = app.Services.GetRequiredService<ISequenceRepository>();
    foreach (var id in ids) await repo.CreateAsync(new CommandSequence { Id = id, Name = "Name-" + id }).ConfigureAwait(true);
  }

  private static async Task<JsonElement> SaveTemplateAsync(HttpClient client, string name, bool overwrite, params object[] entries) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new { name, entries, overwrite }).ConfigureAwait(true);
    resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
    return await BodyAsync(resp).ConfigureAwait(true);
  }

  /// <summary>Creates a template of the given sequences and a queue that links to it, with a live run handle and a plan.</summary>
  private static async Task<Live> StartLiveAsync(WebApplicationFactory<Program> app, HttpClient client, params string[] sequences) {
    await CreateSequencesAsync(app, sequences).ConfigureAwait(true);
    var saved = await SaveTemplateAsync(client, "Live template", overwrite: false,
      sequences.Select(s => (object)new { sequenceId = s, scheduleType = "OncePerRun" }).ToArray()).ConfigureAwait(true);
    var templateId = saved.GetProperty("id").GetString()!;
    var created = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Live farm", emulatorSerial = "emu-offline", cycleExecution = true }).ConfigureAwait(true);
    var queueId = (await BodyAsync(created).ConfigureAwait(true)).GetProperty("id").GetString()!;
    (await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId }).ConfigureAwait(true))
      .StatusCode.Should().Be(HttpStatusCode.OK);

    var template = (await app.Services.GetRequiredService<IQueueTemplateRepository>().GetAsync(templateId).ConfigureAwait(true))!;
    var handle = new QueueRunHandle { QueueId = queueId, Cts = new CancellationTokenSource() };
    handle.RunStartedAt = Start;
    handle.Schedule = new QueueRunSchedule(QueueRunPlan.Build(template.Entries, 0), Start, cycling: true);
    var registry = app.Services.GetRequiredService<IQueueRunRegistry>();
    var runtime = app.Services.GetRequiredService<IQueueRuntimeStore>();
    registry.TryAdd(queueId, handle).Should().BeTrue();
    runtime.ReplaceEntries(queueId, template.Entries.Select(e => new QueueEntry { EntryId = e.EntryId, SequenceId = e.SequenceId }));
    runtime.SetStatus(queueId, QueueExecutionStatus.Running);
    return new Live { QueueId = queueId, TemplateId = templateId, Handle = handle, EntryIds = template.Entries.Select(e => e.EntryId).ToList() };
  }

  private static void Stop(WebApplicationFactory<Program> app, Live live) {
    app.Services.GetRequiredService<IQueueRuntimeStore>().SetStatus(live.QueueId, QueueExecutionStatus.Stopped);
    app.Services.GetRequiredService<IQueueRunRegistry>().Remove(live.QueueId, out _);
    live.Handle.Cts.Dispose();
  }

  private static void ReachThePause(Live live) {
    live.Handle.RequestOperatorPause(Start);
    _ = live.Handle.WaitAtOperatorGateAsync(Start, CancellationToken.None);
    live.Handle.OperatorPause.Should().Be(OperatorPauseState.Paused);
  }

  private static async Task<JsonElement> DiffAsync(HttpClient client, string queueId) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{queueId}/template-diff", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return await BodyAsync(resp).ConfigureAwait(true);
  }

  private static Task<HttpResponseMessage> ApplyAsync(HttpClient client, string queueId, string? baseToken, params string[] keys) =>
    client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/apply-template", UriKind.Relative), new { baseToken, selectedKeys = keys });

  private static async Task<string> ErrorCodeAsync(HttpResponseMessage resp) =>
    (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("error").GetProperty("code").GetString()!;

  [Fact]
  public async Task DiffListsTheDifferencesBetweenTheRunAndTheTemplate() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    await CreateSequencesAsync(app, "seq-c").ConfigureAwait(true);
    var live = await StartLiveAsync(app, client, "seq-a", "seq-b").ConfigureAwait(true);
    try {
      await SaveTemplateAsync(client, "Live template", overwrite: true,
        new { entryId = live.EntryIds[0], sequenceId = "seq-a", scheduleType = "OncePerRun", enabled = false },
        new { entryId = live.EntryIds[1], sequenceId = "seq-b", scheduleType = "OncePerRun" },
        new { sequenceId = "seq-c", scheduleType = "OncePerRun" }).ConfigureAwait(true);

      var diff = await DiffAsync(client, live.QueueId).ConfigureAwait(true);

      diff.GetProperty("needsUpgrade").GetBoolean().Should().BeFalse();
      diff.GetProperty("baseToken").GetString().Should().StartWith("sha256:");
      var keys = diff.GetProperty("differences").EnumerateArray().Select(d => d.GetProperty("key").GetString()).ToList();
      keys.Should().HaveCount(2);
      keys[0].Should().Be("disabled:" + live.EntryIds[0]);
      keys[1].Should().StartWith("added:");
      var added = diff.GetProperty("differences")[1];
      added.GetProperty("sequenceName").GetString().Should().Be("Name-seq-c");
      added.GetProperty("selectedByDefault").GetBoolean().Should().BeTrue();
      added.GetProperty("affectsSchedule").GetBoolean().Should().BeTrue();
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task DiffOfAQueueThatDoesNotRunOrHasNoTemplateGivesTheRightStatus() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var created = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Idle", emulatorSerial = "emu-offline", cycleExecution = true }).ConfigureAwait(true);
    var queueId = (await BodyAsync(created).ConfigureAwait(true)).GetProperty("id").GetString()!;

    var noTemplate = await client.GetAsync(new Uri($"/api/queues/{queueId}/template-diff", UriKind.Relative)).ConfigureAwait(true);
    var unknown = await client.GetAsync(new Uri("/api/queues/nope/template-diff", UriKind.Relative)).ConfigureAwait(true);

    noTemplate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ErrorCodeAsync(noTemplate).ConfigureAwait(true)).Should().Be("no_template");
    unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

    await CreateSequencesAsync(app, "seq-a").ConfigureAwait(true);
    var saved = await SaveTemplateAsync(client, "T", overwrite: false, new { sequenceId = "seq-a" }).ConfigureAwait(true);
    await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId = saved.GetProperty("id").GetString() }).ConfigureAwait(true);
    var notRunning = await client.GetAsync(new Uri($"/api/queues/{queueId}/template-diff", UriKind.Relative)).ConfigureAwait(true);
    notRunning.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await ErrorCodeAsync(notRunning).ConfigureAwait(true)).Should().Be("not_running");
  }

  [Fact]
  public async Task ApplyWhileTheQueueIsNotPausedGives409WithAHint() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a", "seq-b").ConfigureAwait(true);
    try {
      var diff = await DiffAsync(client, live.QueueId).ConfigureAwait(true);

      var resp = await ApplyAsync(client, live.QueueId, diff.GetProperty("baseToken").GetString()).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
      var error = (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("error");
      error.GetProperty("code").GetString().Should().Be("not_paused");
      error.GetProperty("hint").GetString().Should().Be("Pause the queue first.");
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task ApplyWhilePausedPutsTheSelectedDifferencesInTheRunAndTheRuntimeEntries() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    await CreateSequencesAsync(app, "seq-c").ConfigureAwait(true);
    var live = await StartLiveAsync(app, client, "seq-a", "seq-b").ConfigureAwait(true);
    try {
      await SaveTemplateAsync(client, "Live template", overwrite: true,
        new { entryId = live.EntryIds[0], sequenceId = "seq-a", scheduleType = "OncePerRun", enabled = false },
        new { entryId = live.EntryIds[1], sequenceId = "seq-b", scheduleType = "OncePerRun", parameterValues = new[] { new { name = "count", value = "5" } } },
        new { sequenceId = "seq-c", scheduleType = "OncePerRun" }).ConfigureAwait(true);
      ReachThePause(live);
      var diff = await DiffAsync(client, live.QueueId).ConfigureAwait(true);
      var keys = diff.GetProperty("differences").EnumerateArray().Select(d => d.GetProperty("key").GetString()!).ToArray();
      keys.Should().HaveCount(3);

      var resp = await ApplyAsync(client, live.QueueId, diff.GetProperty("baseToken").GetString(), keys).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
      var body = await BodyAsync(resp).ConfigureAwait(true);
      body.GetProperty("planVersion").GetInt32().Should().Be(1);
      body.GetProperty("applied").GetArrayLength().Should().Be(3);

      var plan = live.Handle.Schedule!.Plan;
      plan.Version.Should().Be(1);
      plan.AllEntries.Select(e => e.SequenceId).Should().Equal("seq-a", "seq-b", "seq-c");
      plan.Entries.Select(e => e.SequenceId).Should().Equal("seq-b", "seq-c");
      plan.AllEntries[1].ParameterValues.Single().Value.Should().Be("5");
      var runtime = app.Services.GetRequiredService<IQueueRuntimeStore>().GetEntries(live.QueueId);
      runtime.Select(e => e.SequenceId).Should().Equal("seq-a", "seq-b", "seq-c");
      runtime.Select(e => e.EntryId).Should().Equal(plan.AllKeys);
      live.Handle.OperatorPause.Should().Be(OperatorPauseState.Paused, "the apply does not resume the queue");

      (await DiffAsync(client, live.QueueId).ConfigureAwait(true)).GetProperty("differences").GetArrayLength().Should().Be(0);
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task ANewOrderIsOneDifferenceAndTheApplyPutsTheEntriesInTheOrderOfTheTemplate() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a", "seq-b", "seq-c").ConfigureAwait(true);
    try {
      await SaveTemplateAsync(client, "Live template", overwrite: true,
        new { entryId = live.EntryIds[2], sequenceId = "seq-c" },
        new { entryId = live.EntryIds[0], sequenceId = "seq-a" },
        new { entryId = live.EntryIds[1], sequenceId = "seq-b" }).ConfigureAwait(true);
      ReachThePause(live);
      var diff = await DiffAsync(client, live.QueueId).ConfigureAwait(true);
      var differences = diff.GetProperty("differences");
      differences.GetArrayLength().Should().Be(1);
      differences[0].GetProperty("key").GetString().Should().Be("order:all");
      differences[0].GetProperty("selectedByDefault").GetBoolean().Should().BeTrue();

      var resp = await ApplyAsync(client, live.QueueId, diff.GetProperty("baseToken").GetString(), "order:all").ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
      live.Handle.Schedule!.Plan.AllKeys.Should().Equal(live.EntryIds[2], live.EntryIds[0], live.EntryIds[1]);
      app.Services.GetRequiredService<IQueueRuntimeStore>().GetEntries(live.QueueId).Select(e => e.SequenceId)
        .Should().Equal("seq-c", "seq-a", "seq-b");
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task AnEmptySelectionAppliesNothingAndKeepsThePlanVersion() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a").ConfigureAwait(true);
    try {
      ReachThePause(live);
      var diff = await DiffAsync(client, live.QueueId).ConfigureAwait(true);

      var resp = await ApplyAsync(client, live.QueueId, diff.GetProperty("baseToken").GetString()).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.OK);
      (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("planVersion").GetInt32().Should().Be(0);
      live.Handle.Schedule!.Plan.Version.Should().Be(0);
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task OnlyTheSelectedKeysGoIn() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a", "seq-b").ConfigureAwait(true);
    try {
      await SaveTemplateAsync(client, "Live template", overwrite: true,
        new { entryId = live.EntryIds[0], sequenceId = "seq-a", enabled = false },
        new { entryId = live.EntryIds[1], sequenceId = "seq-b", enabled = false }).ConfigureAwait(true);
      ReachThePause(live);
      var diff = await DiffAsync(client, live.QueueId).ConfigureAwait(true);

      var resp = await ApplyAsync(client, live.QueueId, diff.GetProperty("baseToken").GetString(), "disabled:" + live.EntryIds[0]).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.OK);
      live.Handle.Schedule!.Plan.Entries.Select(e => e.SequenceId).Should().Equal("seq-b");
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task AStaleTokenGives409TemplateChangedAndChangesNothing() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a", "seq-b").ConfigureAwait(true);
    try {
      ReachThePause(live);
      var oldToken = (await DiffAsync(client, live.QueueId).ConfigureAwait(true)).GetProperty("baseToken").GetString();
      await SaveTemplateAsync(client, "Live template", overwrite: true,
        new { entryId = live.EntryIds[0], sequenceId = "seq-a", enabled = false },
        new { entryId = live.EntryIds[1], sequenceId = "seq-b" }).ConfigureAwait(true);

      var resp = await ApplyAsync(client, live.QueueId, oldToken, "disabled:" + live.EntryIds[0]).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
      (await ErrorCodeAsync(resp).ConfigureAwait(true)).Should().Be("template_changed");
      live.Handle.Schedule!.Plan.Version.Should().Be(0);
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task AnApplyThatRunsAtTheSameTimeGives409ApplyInProgress() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a").ConfigureAwait(true);
    try {
      ReachThePause(live);
      var token = (await DiffAsync(client, live.QueueId).ConfigureAwait(true)).GetProperty("baseToken").GetString();
      live.Handle.TryEnterApply().Should().BeTrue();

      var resp = await ApplyAsync(client, live.QueueId, token).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
      (await ErrorCodeAsync(resp).ConfigureAwait(true)).Should().Be("apply_in_progress");
      live.Handle.ExitApply();
      (await ApplyAsync(client, live.QueueId, token).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.OK, "the lock is free again");
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task AKeyThatIsNoDifferenceGives400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a").ConfigureAwait(true);
    try {
      ReachThePause(live);
      var token = (await DiffAsync(client, live.QueueId).ConfigureAwait(true)).GetProperty("baseToken").GetString();

      var resp = await ApplyAsync(client, live.QueueId, token, "disabled:nope").ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      (await ErrorCodeAsync(resp).ConfigureAwait(true)).Should().Be("invalid_request");
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task AMissingTokenGives400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a").ConfigureAwait(true);
    try {
      ReachThePause(live);

      var resp = await ApplyAsync(client, live.QueueId, null).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task AnInvalidPartRefusesTheWholeChangeAndTheQueueStaysTheSame() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var live = await StartLiveAsync(app, client, "seq-a", "seq-b").ConfigureAwait(true);
    try {
      // The new entry uses a sequence that does not exist. The disable of the first entry is valid.
      await SaveTemplateAsync(client, "Live template", overwrite: true,
        new { entryId = live.EntryIds[0], sequenceId = "seq-a", enabled = false },
        new { entryId = live.EntryIds[1], sequenceId = "seq-b" },
        new { sequenceId = "seq-gone" }).ConfigureAwait(true);
      ReachThePause(live);
      var diff = await DiffAsync(client, live.QueueId).ConfigureAwait(true);
      var keys = diff.GetProperty("differences").EnumerateArray().Select(d => d.GetProperty("key").GetString()!).ToArray();
      var runtimeBefore = app.Services.GetRequiredService<IQueueRuntimeStore>().GetEntries(live.QueueId).Select(e => e.EntryId).ToList();

      var resp = await ApplyAsync(client, live.QueueId, diff.GetProperty("baseToken").GetString(), keys).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      live.Handle.Schedule!.Plan.Version.Should().Be(0);
      live.Handle.Schedule.Plan.Entries.Should().HaveCount(2, "the valid disable did not go in either");
      app.Services.GetRequiredService<IQueueRuntimeStore>().GetEntries(live.QueueId).Select(e => e.EntryId).Should().Equal(runtimeBefore);
    }
    finally { Stop(app, live); }
  }

  [Fact]
  public async Task ATemplateThatNeedsAnUpgradeGives409NeedsUpgrade() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    await CreateSequencesAsync(app, "seq-a").ConfigureAwait(true);
    var dir = Path.Combine(_dataDir, "queue-templates");
    Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir, "oldtpl.json"), "{\"Id\":\"oldtpl\",\"Name\":\"Old\",\"Entries\":[{\"SequenceId\":\"seq-a\",\"ScheduleType\":\"OncePerRun\"}]}").ConfigureAwait(true);
    var created = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Old farm", emulatorSerial = "emu-offline", cycleExecution = true }).ConfigureAwait(true);
    var queueId = (await BodyAsync(created).ConfigureAwait(true)).GetProperty("id").GetString()!;
    await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId = "oldtpl" }).ConfigureAwait(true);
    var template = (await app.Services.GetRequiredService<IQueueTemplateRepository>().GetAsync("oldtpl").ConfigureAwait(true))!;
    var handle = new QueueRunHandle { QueueId = queueId, Cts = new CancellationTokenSource() };
    handle.Schedule = new QueueRunSchedule(QueueRunPlan.Build(template.Entries, 0), Start, cycling: true);
    app.Services.GetRequiredService<IQueueRunRegistry>().TryAdd(queueId, handle).Should().BeTrue();
    app.Services.GetRequiredService<IQueueRuntimeStore>().SetStatus(queueId, QueueExecutionStatus.Running);
    try {
      var diff = await DiffAsync(client, queueId).ConfigureAwait(true);
      diff.GetProperty("needsUpgrade").GetBoolean().Should().BeTrue();
      diff.GetProperty("differences").GetArrayLength().Should().Be(0);

      var resp = await ApplyAsync(client, queueId, diff.GetProperty("baseToken").GetString()).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
      var error = (await BodyAsync(resp).ConfigureAwait(true)).GetProperty("error");
      error.GetProperty("code").GetString().Should().Be("needs_upgrade");
      error.GetProperty("hint").GetString().Should().Be("Upgrade the template first.");
    }
    finally {
      app.Services.GetRequiredService<IQueueRuntimeStore>().SetStatus(queueId, QueueExecutionStatus.Stopped);
      app.Services.GetRequiredService<IQueueRunRegistry>().Remove(queueId, out _);
      handle.Cts.Dispose();
    }
  }

  [Fact]
  public async Task OpenApiDescribesTheTwoEndpoints() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var doc = await BodyAsync(await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);

    var paths = doc.GetProperty("paths");
    paths.TryGetProperty("/api/queues/{id}/template-diff", out _).Should().BeTrue();
    paths.TryGetProperty("/api/queues/{id}/apply-template", out _).Should().BeTrue();
    paths.TryGetProperty("/api/queue-templates/{id}/upgrade", out _).Should().BeTrue();
  }
}
