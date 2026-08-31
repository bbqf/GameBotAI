using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

/// <summary>
/// Feature 079 wire contract: the queue-start refusal when another queue holds the emulator, and the
/// monitor's new device field (contracts/api.md sections 1 and 2).
/// </summary>
public sealed class QueueConcurrencyContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevDataDir;
  private readonly string _dataDir;

  public QueueConcurrencyContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");

    // Isolate persistence: these tests create queues, templates and execution-log entries, and must
    // not read or write the developer's real data directory.
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _dataDir = Path.Combine(Path.GetTempPath(), "GameBotContractTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_dataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _dataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _dataDir);
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", null);
    try { Directory.Delete(_dataDir, recursive: true); }
    catch (IOException) { /* best effort */ }
    catch (UnauthorizedAccessException) { /* best effort */ }
    GC.SuppressFinalize(this);
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateRunnableQueueAsync(HttpClient client, string name, string serial) {
    var created = await client.PostAsJsonAsync("/api/queues",
      new { name, emulatorSerial = serial, cycleExecution = true }).ConfigureAwait(true);
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;

    var tplResp = await client.PostAsJsonAsync("/api/queue-templates",
      new { name = "Tpl-" + Guid.NewGuid().ToString("N"), entries = new[] { new { sequenceId = "seq-loop" } }, overwrite = false }).ConfigureAwait(true);
    var tpl = JsonDocument.Parse(await tplResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
    await client.PutAsJsonAsync($"/api/queues/{id}/template", new { templateId = tpl }).ConfigureAwait(true);
    return id;
  }

  private static async Task<JsonElement> MonitorAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}/monitor", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();
  }

  private static async Task WaitForRunningAsync(HttpClient client, string id, int timeoutMs = 8000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if ((await MonitorAsync(client, id).ConfigureAwait(true)).GetProperty("running").GetBoolean()) return;
      await Task.Delay(25).ConfigureAwait(true);
    }
  }

  [Fact] // contracts/api.md §1: 409 device_in_use, naming the device and the holder
  public async Task StartOnAClaimedDeviceReturns409DeviceInUse() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var holder = await CreateRunnableQueueAsync(client, "Holder", "emu-contract-shared").ConfigureAwait(true);
    var other = await CreateRunnableQueueAsync(client, "Other", "emu-contract-shared").ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{holder}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForRunningAsync(client, holder).ConfigureAwait(true);

    var refused = await client.PostAsync(new Uri($"/api/queues/{other}/start", UriKind.Relative), null).ConfigureAwait(true);

    refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var error = JsonDocument.Parse(await refused.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("device_in_use");
    error.GetProperty("message").GetString()!
      .Should().Contain("emu-contract-shared").And.Contain("Holder");

    await client.PostAsync(new Uri($"/api/queues/{holder}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact] // contracts/api.md §1: 'already_running' stays a distinct code
  public async Task RestartingTheSameQueueReturns409AlreadyRunning() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateRunnableQueueAsync(client, "Solo", "emu-contract-solo").ConfigureAwait(true);
    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForRunningAsync(client, id).ConfigureAwait(true);

    var again = await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);

    again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    JsonDocument.Parse(await again.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("already_running");

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }

  [Fact] // contracts/api.md §2: deviceSerial is present, and null when the queue is not running
  public async Task MonitorExposesDeviceSerialOnlyWhileRunning() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateRunnableQueueAsync(client, "Monitored", "emu-contract-monitor").ConfigureAwait(true);

    var idle = await MonitorAsync(client, id).ConfigureAwait(true);
    idle.TryGetProperty("deviceSerial", out var idleSerial).Should().BeTrue("the field is part of the contract");
    idleSerial.ValueKind.Should().Be(JsonValueKind.Null);

    await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null).ConfigureAwait(true);
    await WaitForRunningAsync(client, id).ConfigureAwait(true);

    (await MonitorAsync(client, id).ConfigureAwait(true))
      .GetProperty("deviceSerial").GetString().Should().Be("emu-contract-monitor");

    await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null).ConfigureAwait(true);
  }
}
