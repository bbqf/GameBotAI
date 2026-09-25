#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.ContractTests.Sessions;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.Liveness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Feature 106 (FR-015 to FR-018, contract <c>queue-device-liveness.md</c>): <c>health.deviceLiveness</c>
/// of a running queue, the held firing entry and the fault cycle. These tests start real runs, so each
/// test class uses its own data folder (the contract tests share one bin data folder).
/// </summary>
public sealed class QueueDeviceLivenessContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevDataDir;
  private readonly string _dataDir;

  public QueueDeviceLivenessContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
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

  private static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  /// <summary>Writes a template and a queue that uses it, and returns the queue ID.</summary>
  private static async Task<string> CreateQueueAsync(WebApplicationFactory<Program> app, QueueTemplateEntry entry, bool cycle) {
    var templates = app.Services.GetRequiredService<IQueueTemplateRepository>();
    var template = new QueueTemplate { Id = "tpl-" + Guid.NewGuid().ToString("N"), Name = "Liveness " + Guid.NewGuid().ToString("N") };
    template.Entries.Add(entry);
    template = await templates.CreateAsync(template);
    var queues = app.Services.GetRequiredService<IQueueRepository>();
    var queue = await queues.CreateAsync(new ExecutionQueue {
      Id = "q-" + Guid.NewGuid().ToString("N"),
      Name = "Liveness " + Guid.NewGuid().ToString("N"),
      EmulatorSerial = "emu-liveness-" + Guid.NewGuid().ToString("N")[..8],
      CycleExecution = cycle,
      LinkedTemplateId = template.Id
    });
    return queue.Id;
  }

  private static async Task<JsonElement> DetailAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative));
    var body = await resp.Content.ReadAsStringAsync();
    resp.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return JsonDocument.Parse(body).RootElement.Clone();
  }

  private static async Task<JsonElement> WaitForHealthAsync(HttpClient client, string id, Func<JsonElement, bool> condition) {
    var sw = System.Diagnostics.Stopwatch.StartNew();
    JsonElement detail;
    do {
      detail = await DetailAsync(client, id);
      var health = detail.GetProperty("health");
      if (health.ValueKind == JsonValueKind.Object && condition(health)) return detail;
      await Task.Delay(50);
    } while (sw.ElapsedMilliseconds < 10000);
    return detail;
  }

  [Fact]
  public async Task AQueueOnAStubSessionReportsUnknownAndNoHeldFirings() {
    using var app = new WebApplicationFactory<Program>();
    var client = Client(app);
    // A relative timer far in the future keeps a non-cycling run alive, and nothing fires.
    var id = await CreateQueueAsync(app, new QueueTemplateEntry {
      SequenceId = "seq-" + Guid.NewGuid().ToString("N"),
      ScheduleType = ScheduleType.Timer,
      TimerRelativeOffset = TimeSpan.FromHours(1)
    }, cycle: false);

    (await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      var detail = await WaitForHealthAsync(client, id, h => h.GetProperty("deviceLiveness").ValueKind == JsonValueKind.Object);

      var liveness = detail.GetProperty("health").GetProperty("deviceLiveness");
      liveness.GetProperty("state").GetString().Should().Be("unknown");
      liveness.GetProperty("gatedFirings").GetInt32().Should().Be(0);
      foreach (var field in new[] { "state", "reason", "notLiveSince", "stale", "frameAgeMs", "unchangedMs", "gatedFirings" }) {
        liveness.TryGetProperty(field, out _).Should().BeTrue(field);
      }
    }
    finally {
      await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null);
    }
  }

  [Fact]
  public async Task ADeviceThatIsNotLiveShowsInHealthInTheLogAndInTheFailureCount() {
    var liveness = new FixedLivenessService {
      Report = FixedLivenessService.NotLive(DeviceLivenessReasons.CaptureStalled),
      Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 50, QueueGracePeriodMs = 200 }
    };
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = baseFactory.WithWebHostBuilder(b => b.ConfigureTestServices(services => {
      services.RemoveAll<ISessionLivenessService>();
      services.AddSingleton<ISessionLivenessService>(liveness);
      services.PostConfigure<DeviceLivenessOptions>(o => {
        o.QueueCheckIntervalMs = 1000;
        o.QueueGracePeriodMs = 200;
      });
    }));
    var client = Client(app);
    var sequenceId = "seq-" + Guid.NewGuid().ToString("N");
    var id = await CreateQueueAsync(app, new QueueTemplateEntry { SequenceId = sequenceId, ScheduleType = ScheduleType.OncePerRun }, cycle: false);

    (await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      var detail = await WaitForHealthAsync(client, id, h => h.GetProperty("consecutiveFailedCycles").GetInt32() > 0);

      detail.GetProperty("status").GetString().Should().Be("Running");
      var health = detail.GetProperty("health");
      health.GetProperty("consecutiveFailedCycles").GetInt32().Should().Be(1);
      var dl = health.GetProperty("deviceLiveness");
      dl.GetProperty("state").GetString().Should().Be("not_live");
      dl.GetProperty("reason").GetString().Should().Be("capture_stalled");
      dl.GetProperty("notLiveSince").ValueKind.Should().Be(JsonValueKind.String);
      dl.GetProperty("stale").GetBoolean().Should().BeTrue();
      dl.GetProperty("frameAgeMs").GetInt64().Should().Be(95012);
      dl.GetProperty("unchangedMs").GetInt64().Should().Be(95012);
      dl.GetProperty("gatedFirings").GetInt32().Should().BeGreaterThan(0);

      var log = app.Services.GetRequiredService<IExecutionLogService>();
      var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", ObjectId = sequenceId, PageSize = 50 });
      page.Items.Should().ContainSingle(e => e.FinalStatus == "failure" && e.Summary == "device_not_live: capture_stalled");
    }
    finally {
      await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null);
    }
  }
}
