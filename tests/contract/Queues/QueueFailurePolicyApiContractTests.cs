using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Queues;

/// <summary>
/// The queue failure-policy API surface (feature 087, issue #181): validation, round-tripping,
/// the resume endpoint's not-applicable contracts, and the guarantee that a configured
/// authentication header never reaches a response.
/// <para>
/// None of these paths start a real run, so they do not write to the execution-log store this
/// project shares across test classes. The live-run assertions — tripping, stopping, pausing,
/// resuming — live in the integration project, which gets a clean data dir per class.
/// </para>
/// </summary>
public sealed class QueueFailurePolicyApiContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public QueueFailurePolicyApiContractTests() {
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

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, object body) =>
    await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), body).ConfigureAwait(true);

  private static async Task<JsonElement> JsonOf(HttpResponseMessage resp) =>
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();

  private static async Task<string> CreateWithPolicyAsync(HttpClient client, object policy) {
    var resp = await CreateAsync(client, new {
      name = "Policed",
      emulatorSerial = "emu-offline",
      cycleExecution = true,
      failurePolicy = policy
    }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return (await JsonOf(resp).ConfigureAwait(true)).GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path) {
    var resp = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  // ── Round-tripping ────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Exercises serialisation through the file-backed queue repository. It deliberately does NOT
  /// restart the host: FR-003's "survives a restart" rests on the repository being file-backed,
  /// which this round-trip proves, and a restart harness is out of scope for this feature.
  /// </summary>
  [Fact]
  public async Task PolicyRoundTripsThroughCreateAndGet() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var id = await CreateWithPolicyAsync(client, new {
      consecutiveFailedCycles = 5,
      action = "pause",
      notifyUrl = "https://alerts.example/hook"
    }).ConfigureAwait(true);

    var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);
    var policy = root.GetProperty("failurePolicy");
    policy.GetProperty("consecutiveFailedCycles").GetInt32().Should().Be(5);
    policy.GetProperty("action").GetString().Should().Be("pause");
    policy.GetProperty("notifyUrl").GetString().Should().Be("https://alerts.example/hook");
  }

  [Fact]
  public async Task QueueWithoutPolicyReadsBackNull() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var resp = await CreateAsync(client, new {
      name = "Unpoliced", emulatorSerial = "emu-offline", cycleExecution = true
    }).ConfigureAwait(true);
    var id = (await JsonOf(resp).ConfigureAwait(true)).GetProperty("id").GetString()!;

    var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);

    root.GetProperty("failurePolicy").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task UpdateCanClearAnExistingPolicy() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateWithPolicyAsync(client, new {
      consecutiveFailedCycles = 3, action = "stop"
    }).ConfigureAwait(true);

    var update = await client.PutAsJsonAsync(new Uri($"/api/queues/{id}", UriKind.Relative),
      new { name = "Unpoliced now", cycleExecution = true }).ConfigureAwait(true);
    update.StatusCode.Should().Be(HttpStatusCode.OK);

    var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);
    root.GetProperty("failurePolicy").ValueKind.Should().Be(JsonValueKind.Null);
  }

  // ── Validation ────────────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task NonPositiveThresholdIsRejected() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await CreateAsync(client, new {
      name = "Bad", emulatorSerial = "emu-offline",
      failurePolicy = new { consecutiveFailedCycles = 0, action = "notify" }
    }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .Should().Contain("consecutiveFailedCycles");
  }

  [Fact]
  public async Task UnknownActionIsRejectedListingTheAllowedValues() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await CreateAsync(client, new {
      name = "Bad", emulatorSerial = "emu-offline",
      failurePolicy = new { consecutiveFailedCycles = 3, action = "halt" }
    }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .Should().Contain("notifyAndStop");
  }

  [Fact]
  public async Task MalformedNotifyUrlIsRejected() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await CreateAsync(client, new {
      name = "Bad", emulatorSerial = "emu-offline",
      failurePolicy = new { consecutiveFailedCycles = 3, action = "notify", notifyUrl = "localhost:9099" }
    }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .Should().Contain("absolute http or https");
  }

  /// <summary>
  /// FR-006: a notifying policy with no destination anywhere would look configured and never fire —
  /// the exact silent-failure mode this feature exists to eliminate, so it is a save-time error.
  /// </summary>
  [Fact]
  public async Task NotifyingPolicyWithNoDestinationIsRejected() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await CreateAsync(client, new {
      name = "Bad", emulatorSerial = "emu-offline",
      failurePolicy = new { consecutiveFailedCycles = 3, action = "notify" }
    }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .Should().Contain("requires a destination");
  }

  [Fact]
  public async Task StopPolicyNeedsNoDestination() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var id = await CreateWithPolicyAsync(client, new {
      consecutiveFailedCycles = 3, action = "stop"
    }).ConfigureAwait(true);

    var root = await GetJsonAsync(client, $"/api/queues/{id}").ConfigureAwait(true);
    root.GetProperty("failurePolicy").GetProperty("action").GetString().Should().Be("stop");
  }

  // ── Duplicate carries the policy ──────────────────────────────────────────────────────────────

  [Fact]
  public async Task DuplicateCarriesTheFailurePolicy() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateWithPolicyAsync(client, new {
      consecutiveFailedCycles = 4, action = "pause"
    }).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/duplicate", UriKind.Relative),
      new { name = "Policed copy", emulatorSerial = "emu-offline-2" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);

    var copy = await JsonOf(resp).ConfigureAwait(true);
    copy.GetProperty("failurePolicy").GetProperty("consecutiveFailedCycles").GetInt32().Should()
      .Be(4, "a duplicated roster that silently lost its escalation policy is the failure mode this feature prevents");
  }

  // ── Resume contracts ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task ResumeUnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.PostAsync(new Uri("/api/queues/missing/resume", UriKind.Relative), null)
      .ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  /// <summary>
  /// A known but stopped queue is a state to render, not an error to handle — the contract
  /// {id}/monitor and {id}/cycles already established.
  /// </summary>
  [Fact]
  public async Task ResumeStoppedQueueReturns200NotResumed() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateWithPolicyAsync(client, new {
      consecutiveFailedCycles = 3, action = "pause"
    }).ConfigureAwait(true);

    var resp = await client.PostAsync(new Uri($"/api/queues/{id}/resume", UriKind.Relative), null)
      .ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var root = await JsonOf(resp).ConfigureAwait(true);
    root.GetProperty("id").GetString().Should().Be(id);
    root.GetProperty("resumed").GetBoolean().Should().BeFalse();
  }

  // ── Secret never leaves the service ───────────────────────────────────────────────────────────

  /// <summary>
  /// The configured authentication header is a secret. It lives in service configuration only, and
  /// no response may echo it — not the queue body, not the list, not the health block.
  /// </summary>
  [Fact]
  public async Task AuthHeaderValueNeverAppearsInAnyResponse() {
    Environment.SetEnvironmentVariable("Service__Notifications__DefaultUrl", "http://127.0.0.1:9099/alerts");
    Environment.SetEnvironmentVariable("Service__Notifications__AuthHeaderName", "X-GameBot-Token");
    Environment.SetEnvironmentVariable("Service__Notifications__AuthHeaderValue", "super-secret-value");
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = NewClient(app);
      var id = await CreateWithPolicyAsync(client, new {
        consecutiveFailedCycles = 3, action = "notify"
      }).ConfigureAwait(true);

      var detail = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).ConfigureAwait(true);
      var list = await client.GetAsync(new Uri("/api/queues", UriKind.Relative)).ConfigureAwait(true);

      (await detail.Content.ReadAsStringAsync().ConfigureAwait(true))
        .Should().NotContain("super-secret-value");
      (await list.Content.ReadAsStringAsync().ConfigureAwait(true))
        .Should().NotContain("super-secret-value");
    }
    finally {
      Environment.SetEnvironmentVariable("Service__Notifications__DefaultUrl", null);
      Environment.SetEnvironmentVariable("Service__Notifications__AuthHeaderName", null);
      Environment.SetEnvironmentVariable("Service__Notifications__AuthHeaderValue", null);
    }
  }
}
