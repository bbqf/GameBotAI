using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Asserts the /api/queues request/response shapes match contracts/queues-api.md.
/// </summary>
public sealed class QueuesApiContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public QueuesApiContractTests() {
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
  public async Task QueueLifecycleContractIsExposed() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Create -> QueueResponse shape
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Farm", emulatorSerial = "emu-1", cycleExecution = true }).ConfigureAwait(true);
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    foreach (var field in new[] { "id", "name", "emulatorSerial", "cycleExecution", "status", "entryCount" }) {
      created.TryGetProperty(field, out _).Should().BeTrue($"QueueResponse must expose '{field}'");
    }
    var id = created.GetProperty("id").GetString();

    // Add entry -> QueueEntryResponse shape
    var entryResp = await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/entries", UriKind.Relative), new { sequenceId = "seq-x" }).ConfigureAwait(true);
    entryResp.StatusCode.Should().Be(HttpStatusCode.Created);
    var entry = JsonDocument.Parse(await entryResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    foreach (var field in new[] { "entryId", "sequenceId", "sequenceName", "stale" }) {
      entry.TryGetProperty(field, out _).Should().BeTrue($"QueueEntryResponse must expose '{field}'");
    }

    // Detail -> QueueDetailResponse shape (entries array present)
    var detail = JsonDocument.Parse(await (await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true)).Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    detail.GetProperty("entries").ValueKind.Should().Be(JsonValueKind.Array);

    // Start/stop -> 200 with QueueResponse
    (await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.OK);

    // Delete -> 204
    (await client.DeleteAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
  }
}
