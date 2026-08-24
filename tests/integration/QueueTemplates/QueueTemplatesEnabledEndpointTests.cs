using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.QueueTemplates;

/// <summary>
/// Round-trip coverage for the per-entry <c>enabled</c> flag (feature 077): save persists it,
/// read returns it, omission defaults to true, and toggling it does not disturb schedule fields.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QueueTemplatesEnabledEndpointTests {
  public QueueTemplatesEnabledEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<JsonElement> BodyAsync(HttpResponseMessage resp) =>
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();

  [Fact]
  public async Task SaveWithDisabledEntryPersistsAndReadsBackDisabled() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var entries = new object[] {
      new { sequenceId = "seq-a", scheduleType = "OncePerRun", enabled = false },
      new { sequenceId = "seq-b", scheduleType = "OncePerRun", enabled = true },
    };

    var save = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Toggle Template", entries, overwrite = false }).ConfigureAwait(true);
    save.StatusCode.Should().Be(HttpStatusCode.Created);
    var saved = await BodyAsync(save).ConfigureAwait(true);
    var id = saved.GetProperty("id").GetString();

    var read = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    read.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await BodyAsync(read).ConfigureAwait(true);
    var readEntries = body.GetProperty("entries");
    readEntries.GetArrayLength().Should().Be(2);
    // Position and disabled state both preserved.
    readEntries[0].GetProperty("sequenceId").GetString().Should().Be("seq-a");
    readEntries[0].GetProperty("enabled").GetBoolean().Should().BeFalse();
    readEntries[1].GetProperty("enabled").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task SaveWithEnabledOmittedDefaultsToTrue() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    // No 'enabled' key at all (legacy client shape).
    var entries = new object[] { new { sequenceId = "seq-a", scheduleType = "OncePerRun" } };

    var save = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Legacy Shape", entries, overwrite = false }).ConfigureAwait(true);
    var saved = await BodyAsync(save).ConfigureAwait(true);
    var id = saved.GetProperty("id").GetString();

    var read = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    var body = await BodyAsync(read).ConfigureAwait(true);
    body.GetProperty("entries")[0].GetProperty("enabled").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task DisablingTimerEntryPreservesScheduleFields() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    // FR-008: toggling enabled must not alter scheduleType / timer fields.
    var entries = new object[] {
      new { sequenceId = "seq-a", scheduleType = "Timer", timerRelativeOffset = "00:30:00", enabled = false },
    };

    var save = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Timer Disabled", entries, overwrite = false }).ConfigureAwait(true);
    save.StatusCode.Should().Be(HttpStatusCode.Created);
    var saved = await BodyAsync(save).ConfigureAwait(true);
    var id = saved.GetProperty("id").GetString();

    var read = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    var entry = (await BodyAsync(read).ConfigureAwait(true)).GetProperty("entries")[0];
    entry.GetProperty("enabled").GetBoolean().Should().BeFalse();
    entry.GetProperty("scheduleType").GetString().Should().Be("Timer");
    entry.GetProperty("timerRelativeOffset").GetString().Should().Be("00:30:00");
  }
}
