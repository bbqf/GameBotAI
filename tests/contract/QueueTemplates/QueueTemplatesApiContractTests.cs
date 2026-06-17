using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.QueueTemplates;

/// <summary>
/// Asserts the /api/queue-templates request/response shapes match contracts/queue-templates-api.md.
/// </summary>
public sealed class QueueTemplatesApiContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public QueueTemplatesApiContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task QueueTemplateLifecycleContractIsExposed() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var name = "Contract " + Guid.NewGuid().ToString("N");
    var entries = new[] { new { sequenceId = "seq-x" } };

    // Save (create) -> 201 QueueTemplateDetailResponse shape
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name, entries, overwrite = false }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    foreach (var field in new[] { "id", "name", "entryCount", "createdAt", "updatedAt", "entries" }) {
      created.TryGetProperty(field, out _).Should().BeTrue($"detail must expose '{field}'");
    }
    var entry = created.GetProperty("entries")[0];
    foreach (var field in new[] { "sequenceId", "sequenceName", "stale", "scheduleType", "timerTimeOfDay" }) {
      entry.TryGetProperty(field, out _).Should().BeTrue($"entry must expose '{field}'");
    }
    var id = created.GetProperty("id").GetString();

    // List -> 200 array of summaries
    var listResp = await client.GetAsync(new Uri("/api/queue-templates", UriKind.Relative)).ConfigureAwait(true);
    listResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var list = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    list.ValueKind.Should().Be(JsonValueKind.Array);

    // Detail -> entries array present
    var detail = JsonDocument.Parse(await (await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true)).Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    detail.GetProperty("entries").ValueKind.Should().Be(JsonValueKind.Array);

    // Duplicate name without overwrite -> 409
    var conflict = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name, entries = Array.Empty<object>(), overwrite = false }).ConfigureAwait(true);
    conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

    // Overwrite -> 200
    var overwrite = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name, entries = Array.Empty<object>(), overwrite = true }).ConfigureAwait(true);
    overwrite.StatusCode.Should().Be(HttpStatusCode.OK);

    // Invalid name -> 400
    var invalid = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "bad/name", entries = Array.Empty<object>(), overwrite = false }).ConfigureAwait(true);
    invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Delete -> 204
    (await client.DeleteAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
  }

  [Fact] // feature 059: relative-offset timer mode
  public async Task TimerRelativeOffsetContractIsAcceptedReturnedAndValidated() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var name = "Relative " + Guid.NewGuid().ToString("N");

    // Accepts a Timer entry with timerRelativeOffset and echoes it back; timerTimeOfDay stays null.
    var entries = new[] { new { sequenceId = "seq-x", scheduleType = "Timer", timerRelativeOffset = "00:10:00" } };
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name, entries, overwrite = true }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    var entry = created.GetProperty("entries")[0];
    entry.TryGetProperty("timerRelativeOffset", out _).Should().BeTrue("entry must expose 'timerRelativeOffset'");
    entry.GetProperty("scheduleType").GetString().Should().Be("Timer");
    entry.GetProperty("timerRelativeOffset").GetString().Should().Be("00:10:00");
    entry.GetProperty("timerTimeOfDay").ValueKind.Should().Be(JsonValueKind.Null);

    // Rejects a Timer entry that sets BOTH timer fields -> 400.
    var both = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = name + "b", overwrite = true, entries = new[] { new { sequenceId = "seq-x", scheduleType = "Timer", timerTimeOfDay = "15:30", timerRelativeOffset = "00:10:00" } } }).ConfigureAwait(true);
    both.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Rejects a Timer entry that sets NEITHER timer field -> 400.
    var neither = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = name + "n", overwrite = true, entries = new[] { new { sequenceId = "seq-x", scheduleType = "Timer" } } }).ConfigureAwait(true);
    neither.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Rejects an out-of-range offset -> 400.
    var tooBig = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = name + "x", overwrite = true, entries = new[] { new { sequenceId = "seq-x", scheduleType = "Timer", timerRelativeOffset = "25:00:00" } } }).ConfigureAwait(true);
    tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }
}
