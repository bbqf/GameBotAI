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
/// Feature 098 (#203): a service restart brings back the opted-in queues that were Running, and nothing
/// else. Each test runs two hosts in sequence over one data directory — the second host is the restarted
/// service. ADB runs in stub mode, so a cycling queue linked to a one-entry template holds Running (the
/// same pattern QueueExecutionEndpointTests uses).
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueResumeOnRestartTests {
  private readonly string _dataDir;

  public QueueResumeOnRestartTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    _dataDir = TestEnvironment.PrepareCleanDataDir();
  }

  private string RecordPath => Path.Combine(_dataDir, "queue-run-state.json");

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateCyclingQueueAsync(HttpClient client, string name, string serial, bool resume) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name, emulatorSerial = serial, cycleExecution = true, resumeOnServiceStart = resume }).ConfigureAwait(true);
    var id = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
    var tplResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Tpl-" + Guid.NewGuid().ToString("N"), entries = new[] { new { sequenceId = "seq-loop" } }, overwrite = false }).ConfigureAwait(true);
    var tpl = JsonDocument.Parse(await tplResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
    await client.PutAsJsonAsync(new Uri($"/api/queues/{id}/template", UriKind.Relative), new { templateId = tpl }).ConfigureAwait(true);
    return id;
  }

  private static async Task<string> StatusAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("status").GetString()!;
  }

  private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 30000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if (await condition().ConfigureAwait(true)) return true;
      await Task.Delay(50).ConfigureAwait(true);
    }
    return await condition().ConfigureAwait(true);
  }

  private async Task<string> RecordTextAsync()
    => File.Exists(RecordPath) ? await File.ReadAllTextAsync(RecordPath).ConfigureAwait(true) : string.Empty;

  private static async Task StartAndWaitRunningAsync(HttpClient client, string id) {
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    (await WaitUntilAsync(async () => await StatusAsync(client, id).ConfigureAwait(true) == "Running", 5000).ConfigureAwait(true))
      .Should().BeTrue("the queue must be running before the service restarts");
  }

  [Fact]
  public async Task OptedInRunningQueueIsRunningAgainAfterRestart() {
    string id;
    using (var first = new WebApplicationFactory<Program>()) {
      var client = NewClient(first);
      id = await CreateCyclingQueueAsync(client, "Farm", "emu-resume-1", resume: true).ConfigureAwait(true);
      await StartAndWaitRunningAsync(client, id).ConfigureAwait(true);
    }

    (await RecordTextAsync().ConfigureAwait(true)).Should().Contain(id, "a service shutdown must keep the running record");

    using var second = new WebApplicationFactory<Program>();
    var restarted = NewClient(second);
    (await WaitUntilAsync(async () => await StatusAsync(restarted, id).ConfigureAwait(true) == "Running").ConfigureAwait(true))
      .Should().BeTrue("the opted-in queue must be resumed after the restart");

    await restarted.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact]
  public async Task StoppedAndNotOptedInQueuesStayStoppedAfterRestart() {
    string stoppedId;
    string notOptedInId;
    using (var first = new WebApplicationFactory<Program>()) {
      var client = NewClient(first);
      stoppedId = await CreateCyclingQueueAsync(client, "Stopped", "emu-resume-2", resume: true).ConfigureAwait(true);
      notOptedInId = await CreateCyclingQueueAsync(client, "NotOptedIn", "emu-resume-3", resume: false).ConfigureAwait(true);
      await StartAndWaitRunningAsync(client, stoppedId).ConfigureAwait(true);
      await StartAndWaitRunningAsync(client, notOptedInId).ConfigureAwait(true);
      await client.PostAsync(new Uri($"/api/queues/{stoppedId}/stop", UriKind.Relative), null).ConfigureAwait(true);
    }

    var record = await RecordTextAsync().ConfigureAwait(true);
    record.Should().NotContain(stoppedId, "an operator stop forgets the running record");
    record.Should().Contain(notOptedInId);

    using var second = new WebApplicationFactory<Program>();
    var restarted = NewClient(second);
    (await WaitUntilAsync(async () => !(await RecordTextAsync().ConfigureAwait(true)).Contains(notOptedInId, StringComparison.Ordinal)).ConfigureAwait(true))
      .Should().BeTrue("the resume pass discards the record of a queue that does not opt in");

    (await StatusAsync(restarted, stoppedId).ConfigureAwait(true)).Should().Be("Stopped");
    (await StatusAsync(restarted, notOptedInId).ConfigureAwait(true)).Should().Be("Stopped");
  }
}
