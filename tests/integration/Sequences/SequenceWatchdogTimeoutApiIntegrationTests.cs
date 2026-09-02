using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Round-trips a sequence's per-firing watchdog bound through the API. The bound is only useful if it
/// can actually be set, and the natural way to set one on an existing sequence is a PATCH carrying
/// nothing else — a body with no steps, which the per-step branch never reads.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SequenceWatchdogTimeoutApiIntegrationTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;

  public SequenceWatchdogTimeoutApiIntegrationTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    GC.SuppressFinalize(this);
  }

  private static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static Dictionary<string, object> SequenceBody(string name, int? watchdogTimeoutMs) {
    var body = new Dictionary<string, object> {
      ["name"] = name,
      ["version"] = 1,
      ["steps"] = new object[] {
        new {
          stepId = "wait",
          primitiveAction = new { type = "WaitForImage", schemaVersion = "v1", payload = new { timeoutMs = 0 } }
        }
      }
    };
    if (watchdogTimeoutMs is not null) body["watchdogTimeoutMs"] = watchdogTimeoutMs;
    return body;
  }

  private static async Task<string> CreateSequenceAsync(HttpClient client, int? watchdogTimeoutMs) {
    var resp = await client.PostAsJsonAsync(
      new Uri("/api/sequences", UriKind.Relative), SequenceBody("Watchdog sequence", watchdogTimeoutMs)).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return doc.RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetSequenceAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement.Clone();
  }

  [Fact]
  public async Task WatchdogSurvivesCreateAndRead() {
    using var app = new WebApplicationFactory<Program>();
    var client = Client(app);

    var id = await CreateSequenceAsync(client, 1_200_000).ConfigureAwait(false);

    (await GetSequenceAsync(client, id).ConfigureAwait(false))
      .GetProperty("watchdogTimeoutMs").GetInt32().Should().Be(1_200_000);
  }

  [Fact]
  public async Task SequenceWithoutAWatchdogReportsNone() {
    using var app = new WebApplicationFactory<Program>();
    var client = Client(app);

    var id = await CreateSequenceAsync(client, null).ConfigureAwait(false);

    (await GetSequenceAsync(client, id).ConfigureAwait(false))
      .GetProperty("watchdogTimeoutMs").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact] // The natural way to set one: a PATCH carrying only the watchdog, with no steps at all.
  public async Task PatchCarryingOnlyTheWatchdogIsApplied() {
    using var app = new WebApplicationFactory<Program>();
    var client = Client(app);
    var id = await CreateSequenceAsync(client, null).ConfigureAwait(false);

    using (var patch = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/api/sequences/{id}", UriKind.Relative)) {
      Content = new StringContent("{\"watchdogTimeoutMs\":900000}", Encoding.UTF8, "application/json")
    }) {
      (await client.SendAsync(patch).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    var after = await GetSequenceAsync(client, id).ConfigureAwait(false);
    after.GetProperty("watchdogTimeoutMs").GetInt32().Should().Be(900_000);
    // The steps must survive a PATCH that never mentioned them.
    after.GetProperty("steps").GetArrayLength().Should().Be(1);
  }

  [Fact] // An explicit null hands the sequence back to the queue default.
  public async Task PatchWithNullClearsTheWatchdog() {
    using var app = new WebApplicationFactory<Program>();
    var client = Client(app);
    var id = await CreateSequenceAsync(client, 600_000).ConfigureAwait(false);

    using (var patch = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/api/sequences/{id}", UriKind.Relative)) {
      Content = new StringContent("{\"watchdogTimeoutMs\":null}", Encoding.UTF8, "application/json")
    }) {
      (await client.SendAsync(patch).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    (await GetSequenceAsync(client, id).ConfigureAwait(false))
      .GetProperty("watchdogTimeoutMs").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact] // The cap keeps a typo from handing one sequence an unbounded hold on its emulator.
  public async Task WatchdogBeyondTheCapIsRejected() {
    using var app = new WebApplicationFactory<Program>();
    var client = Client(app);

    var resp = await client.PostAsJsonAsync(
      new Uri("/api/sequences", UriKind.Relative), SequenceBody("Too patient", 31 * 60 * 1000)).ConfigureAwait(false);

    resp.StatusCode.Should().NotBe(HttpStatusCode.Created);
    resp.IsSuccessStatusCode.Should().BeFalse();
  }
}
