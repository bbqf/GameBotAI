using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA2000

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 184: run next, end to end through the real DI graph and the HTTP API. ADB is stubbed. A far relative timer keeps
/// the non-cycling run alive, so the entries can be asked for after the other entries ended.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueRunNextEndToEndTests {
  public QueueRunNextEndToEndTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  // A wait-for-image step with no target and a zero timeout. It is a step that needs no device and always succeeds.
  private static CommandSequence Sequence(string id) {
    var sequence = new CommandSequence { Id = id, Name = id };
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "marker", StepType = SequenceStepType.Action, WaitForImage = new WaitForImageConfig { TimeoutMs = 0 } }
    });
    return sequence;
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> StateOfAsync(HttpClient client, string queueId, string entryId) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{queueId}/monitor", UriKind.Relative)).ConfigureAwait(false);
    var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    return json.GetProperty("entryStates").TryGetProperty(entryId, out var state) ? state.GetString()! : "none";
  }

  private static async Task WaitForStateAsync(HttpClient client, string queueId, string entryId, string expected) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 10000 && await StateOfAsync(client, queueId, entryId).ConfigureAwait(false) != expected) {
      await Task.Delay(50).ConfigureAwait(false);
    }
    (await StateOfAsync(client, queueId, entryId).ConfigureAwait(false)).Should().Be(expected);
  }

  [Fact]
  public async Task RunNextRunsAnIdleEntryAgainAndTheOtherEntriesDoNotRunAgain() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var services = app.Services;
    var sequences = services.GetRequiredService<ISequenceRepository>();
    foreach (var id in new[] { "seq-a", "seq-b", "seq-far" }) await sequences.CreateAsync(Sequence(id)).ConfigureAwait(false);

    var template = new QueueTemplate { Id = "tpl-rn", Name = "T-rn" };
    template.Entries.Add(new QueueTemplateEntry { EntryId = "e-a", SequenceId = "seq-a", ScheduleType = ScheduleType.OncePerRun });
    template.Entries.Add(new QueueTemplateEntry { EntryId = "e-b", SequenceId = "seq-b", ScheduleType = ScheduleType.OncePerRun });
    template.Entries.Add(new QueueTemplateEntry { EntryId = "e-far", SequenceId = "seq-far", ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromHours(1) });
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = "q-rn", Name = "Q-rn", EmulatorSerial = "emu-offline", CycleExecution = false, LinkedTemplateId = template.Id
    }).ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    (await engine.StartAsync("q-rn").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForStateAsync(client, "q-rn", "e-a", "idle").ConfigureAwait(false);
    await WaitForStateAsync(client, "q-rn", "e-b", "idle").ConfigureAwait(false);
    (await StateOfAsync(client, "q-rn", "e-far").ConfigureAwait(false)).Should().Be("scheduled");

    var resp = await client.PostAsJsonAsync(new Uri("/api/queues/q-rn/run-next", UriKind.Relative), new { entryId = "e-a" }).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    // The entry runs and is idle again. The timer entry is not touched.
    await Task.Delay(1200).ConfigureAwait(false);
    await WaitForStateAsync(client, "q-rn", "e-a", "idle").ConfigureAwait(false);
    (await StateOfAsync(client, "q-rn", "e-far").ConfigureAwait(false)).Should().Be("scheduled");

    await engine.StopAsync("q-rn").ConfigureAwait(false);

    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "queue", RootsOnly = true, PageSize = 100 }).ConfigureAwait(false);
    var entry = page.Items.First(e => e.ObjectRef.ObjectId == "q-rn");
    // A, B, and the second run of A. The far timer never fired.
    entry.Summary.Should().Contain("3 sequence(s) executed");
  }

  [Fact]
  public async Task RunNextOnAScheduledEntryIsRefusedWith409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var services = app.Services;
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(Sequence("seq-far")).ConfigureAwait(false);
    var template = new QueueTemplate { Id = "tpl-rn2", Name = "T-rn2" };
    template.Entries.Add(new QueueTemplateEntry { EntryId = "e-far", SequenceId = "seq-far", ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromHours(1) });
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = "q-rn2", Name = "Q-rn2", EmulatorSerial = "emu-offline", CycleExecution = false, LinkedTemplateId = template.Id
    }).ConfigureAwait(false);
    var engine = services.GetRequiredService<IQueueExecutionService>();
    (await engine.StartAsync("q-rn2").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    await WaitForStateAsync(client, "q-rn2", "e-far", "scheduled").ConfigureAwait(false);

    var resp = await client.PostAsJsonAsync(new Uri("/api/queues/q-rn2/run-next", UriKind.Relative), new { entryId = "e-far" }).ConfigureAwait(false);

    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    json.GetProperty("error").GetProperty("code").GetString().Should().Be("entry_scheduled");
    await engine.StopAsync("q-rn2").ConfigureAwait(false);
  }
}
