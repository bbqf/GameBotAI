using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.QueueTemplates;

/// <summary>
/// SC-007: the template picker (list) and template detail (load) remain responsive (&lt;1s)
/// at the target scale of ~50 templates with ~100 entries each.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueTemplatesScaleTests {
  public QueueTemplatesScaleTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task ListAndDetailRespondWithinBudgetAtScale() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var entries = Enumerable.Range(0, 100).Select(e => new { sequenceId = $"seq-{e}" }).ToArray();
    string? firstId = null;
    for (var t = 0; t < 50; t++) {
      var resp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
        new { name = $"Template{t}", entries, overwrite = false }).ConfigureAwait(true);
      var id = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
      firstId ??= id;
    }

    var listSw = Stopwatch.StartNew();
    var listResp = await client.GetAsync(new Uri("/api/queue-templates", UriKind.Relative)).ConfigureAwait(true);
    listSw.Stop();
    listResp.EnsureSuccessStatusCode();
    JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetArrayLength().Should().Be(50);
    listSw.ElapsedMilliseconds.Should().BeLessThan(1000);

    var detailSw = Stopwatch.StartNew();
    var detailResp = await client.GetAsync(new Uri($"/api/queue-templates/{firstId}", UriKind.Relative)).ConfigureAwait(true);
    detailSw.Stop();
    detailResp.EnsureSuccessStatusCode();
    JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("entries").GetArrayLength().Should().Be(100);
    detailSw.ElapsedMilliseconds.Should().BeLessThan(1000);
  }
}
