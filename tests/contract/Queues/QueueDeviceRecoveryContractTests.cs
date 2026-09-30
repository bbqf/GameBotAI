#pragma warning disable CA2007 // test code: no ConfigureAwait
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
/// The <c>deviceRecovery</c> field of a queue (feature 121, contract <c>device-recovery-api.md</c>):
/// round-trip, defaults, the clear rule of <c>PUT</c>, the copy in the duplicate route, and the
/// validation errors. A bad value must give HTTP 400 and never 500. None of these tests starts a run.
/// </summary>
public sealed class QueueDeviceRecoveryContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public QueueDeviceRecoveryContractTests() {
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

  private static async Task<JsonElement> JsonOf(HttpResponseMessage resp) =>
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();

  private static Task<HttpResponseMessage> CreateAsync(HttpClient client, object body) =>
    client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), body);

  private static async Task<string> CreateWithRecoveryAsync(HttpClient client, object recovery, string? instance = "LDPlayer-1") {
    var resp = await CreateAsync(client, new {
      name = "Recovering",
      emulatorSerial = "emu-offline",
      emulatorInstanceName = instance,
      deviceRecovery = recovery
    });
    resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
    return (await JsonOf(resp)).GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetQueueAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return await JsonOf(resp);
  }

  private static async Task<string> ErrorMessageAsync(HttpResponseMessage resp) =>
    (await JsonOf(resp)).GetProperty("error").GetProperty("message").GetString()!;

  [Fact]
  public async Task SettingsRoundTripThroughCreateAndGet() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var id = await CreateWithRecoveryAsync(client, new { action = "reboot-instance", afterMs = 120000, maxAttempts = 3, cooldownMs = 5000 });

    var recovery = (await GetQueueAsync(client, id)).GetProperty("deviceRecovery");
    recovery.GetProperty("action").GetString().Should().Be("reboot-instance");
    recovery.GetProperty("afterMs").GetInt32().Should().Be(120000);
    recovery.GetProperty("maxAttempts").GetInt32().Should().Be(3);
    recovery.GetProperty("cooldownMs").GetInt32().Should().Be(5000);
  }

  [Fact]
  public async Task AbsentMembersTakeTheDefaults() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var id = await CreateWithRecoveryAsync(client, new { action = "reboot-instance" });

    var recovery = (await GetQueueAsync(client, id)).GetProperty("deviceRecovery");
    recovery.GetProperty("afterMs").GetInt32().Should().Be(300000);
    recovery.GetProperty("maxAttempts").GetInt32().Should().Be(2);
    recovery.GetProperty("cooldownMs").GetInt32().Should().Be(180000);
  }

  [Fact]
  public async Task AQueueWithNoFieldReadsBackNull() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var resp = await CreateAsync(client, new { name = "Plain", emulatorSerial = "emu-offline" });
    var id = (await JsonOf(resp)).GetProperty("id").GetString()!;

    (await GetQueueAsync(client, id)).GetProperty("deviceRecovery").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task AnAbsentMemberOnPutClearsTheField() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateWithRecoveryAsync(client, new { action = "reboot-instance" });

    var update = await client.PutAsJsonAsync(new Uri($"/api/queues/{id}", UriKind.Relative),
      new { name = "Plain now", emulatorInstanceName = "LDPlayer-1" });
    update.StatusCode.Should().Be(HttpStatusCode.OK);

    (await GetQueueAsync(client, id)).GetProperty("deviceRecovery").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task PutSetsAndUpdatesTheField() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var resp = await CreateAsync(client, new { name = "Plain", emulatorSerial = "emu-offline", emulatorInstanceName = "LDPlayer-2" });
    var id = (await JsonOf(resp)).GetProperty("id").GetString()!;

    var update = await client.PutAsJsonAsync(new Uri($"/api/queues/{id}", UriKind.Relative), new {
      name = "Recovering now",
      emulatorInstanceName = "LDPlayer-2",
      deviceRecovery = new { action = "reboot-instance", maxAttempts = 4 }
    });
    update.StatusCode.Should().Be(HttpStatusCode.OK);

    var recovery = (await GetQueueAsync(client, id)).GetProperty("deviceRecovery");
    recovery.GetProperty("maxAttempts").GetInt32().Should().Be(4);
    (await JsonOf(update)).GetProperty("deviceRecovery").GetProperty("action").GetString().Should().Be("reboot-instance");
  }

  [Fact]
  public async Task DuplicateCopiesTheField() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateWithRecoveryAsync(client, new { action = "reboot-instance", afterMs = 90000, maxAttempts = 5, cooldownMs = 0 });

    var dup = await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/duplicate", UriKind.Relative),
      new { name = "Copy", emulatorSerial = "emu-copy", emulatorInstanceName = "LDPlayer-3" });

    dup.StatusCode.Should().Be(HttpStatusCode.Created, await dup.Content.ReadAsStringAsync());
    var recovery = (await JsonOf(dup)).GetProperty("deviceRecovery");
    recovery.GetProperty("action").GetString().Should().Be("reboot-instance");
    recovery.GetProperty("afterMs").GetInt32().Should().Be(90000);
    recovery.GetProperty("maxAttempts").GetInt32().Should().Be(5);
    recovery.GetProperty("cooldownMs").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task ListShowsTheField() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateWithRecoveryAsync(client, new { action = "none" });

    var list = await client.GetAsync(new Uri("/api/queues", UriKind.Relative));
    var items = await JsonOf(list);

    var found = false;
    foreach (var item in items.EnumerateArray()) {
      if (item.GetProperty("id").GetString() != id) continue;
      found = true;
      item.GetProperty("deviceRecovery").GetProperty("action").GetString().Should().Be("none");
    }
    found.Should().BeTrue();
  }

  [Theory]
  [InlineData("format-disk", null, null, null, "deviceRecovery.action must be one of: none, reboot-instance (was: 'format-disk')")]
  [InlineData("reboot-instance", 59999, null, null, "deviceRecovery.afterMs must be at least 60000 (was: 59999)")]
  [InlineData("reboot-instance", null, 0, null, "deviceRecovery.maxAttempts must be from 1 to 5 (was: 0)")]
  [InlineData("reboot-instance", null, 6, null, "deviceRecovery.maxAttempts must be from 1 to 5 (was: 6)")]
  [InlineData("reboot-instance", null, null, -1, "deviceRecovery.cooldownMs must be at least 0 (was: -1)")]
  [InlineData("none", 10, null, null, "deviceRecovery.afterMs must be at least 60000 (was: 10)")]
  public async Task AnInvalidValueGivesHttp400WithTheContractText(string action, int? afterMs, int? maxAttempts, int? cooldownMs, string expected) {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await CreateAsync(client, new {
      name = "Bad", emulatorSerial = "emu-offline", emulatorInstanceName = "LDPlayer-1",
      deviceRecovery = new { action, afterMs, maxAttempts, cooldownMs }
    });

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ErrorMessageAsync(resp)).Should().Be(expected);
  }

  [Fact]
  public async Task RebootWithNoInstanceNameGivesHttp400OnCreateAndOnUpdate() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    const string expected = "deviceRecovery.action 'reboot-instance' needs emulatorInstanceName. Set emulatorInstanceName on the queue.";

    var create = await CreateAsync(client, new {
      name = "Bad", emulatorSerial = "emu-offline", deviceRecovery = new { action = "reboot-instance" }
    });
    create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ErrorMessageAsync(create)).Should().Be(expected);

    var plain = await CreateAsync(client, new { name = "Plain", emulatorSerial = "emu-offline" });
    var id = (await JsonOf(plain)).GetProperty("id").GetString()!;
    var update = await client.PutAsJsonAsync(new Uri($"/api/queues/{id}", UriKind.Relative), new {
      name = "Plain", deviceRecovery = new { action = "reboot-instance" }
    });
    update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ErrorMessageAsync(update)).Should().Be(expected);
  }

  [Fact]
  public async Task ARejectedRequestChangesNothing() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateWithRecoveryAsync(client, new { action = "reboot-instance", maxAttempts = 3 });

    var update = await client.PutAsJsonAsync(new Uri($"/api/queues/{id}", UriKind.Relative), new {
      name = "Renamed", emulatorInstanceName = "LDPlayer-1", deviceRecovery = new { action = "reboot-instance", maxAttempts = 9 }
    });

    update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var queue = await GetQueueAsync(client, id);
    queue.GetProperty("name").GetString().Should().Be("Recovering");
    queue.GetProperty("deviceRecovery").GetProperty("maxAttempts").GetInt32().Should().Be(3);
  }

  [Fact]
  public async Task DuplicateWithNoInstanceNameForARebootQueueGivesHttp400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var id = await CreateWithRecoveryAsync(client, new { action = "reboot-instance" });

    var dup = await client.PostAsJsonAsync(new Uri($"/api/queues/{id}/duplicate", UriKind.Relative),
      new { name = "Copy", emulatorSerial = "emu-copy" });

    dup.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

}
