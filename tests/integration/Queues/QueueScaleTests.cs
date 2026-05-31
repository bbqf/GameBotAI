using System;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// SC-007: the Queues list and detail views remain responsive (&lt;1s) at the target
/// scale of ~50 queues with ~100 sequence entries each.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueScaleTests {
  public QueueScaleTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task ListAndDetailRespondWithinBudgetAtScale() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    string? firstId = null;
    for (var q = 0; q < 50; q++) {
      var createResp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new { name = $"Queue{q}", emulatorSerial = "emu-1" }).ConfigureAwait(true);
      var id = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
      firstId ??= id;
      for (var e = 0; e < 100; e++) {
        await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = $"seq-{e}" }).ConfigureAwait(true);
      }
    }

    var listSw = Stopwatch.StartNew();
    var listResp = await client.GetAsync(new Uri("/api/queues", UriKind.Relative)).ConfigureAwait(true);
    listSw.Stop();
    listResp.EnsureSuccessStatusCode();
    JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetArrayLength().Should().Be(50);
    listSw.ElapsedMilliseconds.Should().BeLessThan(1000);

    var detailSw = Stopwatch.StartNew();
    var detailResp = await client.GetAsync(new Uri($"/api/queues/{firstId}", UriKind.Relative)).ConfigureAwait(true);
    detailSw.Stop();
    detailResp.EnsureSuccessStatusCode();
    JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("entries").GetArrayLength().Should().Be(100);
    detailSw.ElapsedMilliseconds.Should().BeLessThan(1000);
  }
}
