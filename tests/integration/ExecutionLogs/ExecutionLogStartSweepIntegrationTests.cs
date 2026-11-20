using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.IntegrationTests.Helpers;
using GameBot.Service.Hosted;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

/// <summary>
/// Feature 198 (B-044, FR-006, FR-006a, FR-007): at service start, each "running" entry of an earlier
/// service process gets the status "failure".
/// </summary>
[Collection("ConfigIsolation")]
public sealed class ExecutionLogStartSweepIntegrationTests {
  private const string QueueId = "q-sweep";
  private const string QueueName = "PNS.Production (Exo)";
  private static readonly TimeSpan PollLimit = TimeSpan.FromSeconds(10);

  [Fact]
  public async Task StartSweepClosesRunningEntriesOfEarlierProcess() {
    var previousAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    var dataDir = TestEnvironment.PrepareCleanDataDir();
    try {
      var (rootId, childId) = await SeedEarlierRunAsync(dataDir).ConfigureAwait(false);

      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var repository = app.Services.GetRequiredService<IExecutionLogRepository>();

      var root = await PollUntilStatusAsync(repository, rootId, "failure").ConfigureAwait(false);
      var child = await PollUntilStatusAsync(repository, childId, "failure").ConfigureAwait(false);

      root.Summary.Should().Be($"Queue '{QueueName}' run ended when the service stopped.");
      child.Summary.Should().Be("Sequence 'Farm' run ended when the service stopped.");

      var response = await client.GetAsync(new Uri($"/api/execution-logs?objectId={QueueId}", UriKind.Relative)).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();
      using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
      var item = doc.RootElement.GetProperty("items").EnumerateArray()
        .Single(i => i.GetProperty("id").GetString() == rootId);
      item.GetProperty("finalStatus").GetString().Should().Be("failure");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", previousAuthToken);
    }
  }

  [Fact]
  public async Task StartSweepKeepsEntriesAfterProcessStart() {
    var dataDir = TestEnvironment.PrepareCleanDataDir();
    var now = DateTimeOffset.UtcNow;
    var processStartUtc = now.AddHours(-2);
    using (var seed = new FileExecutionLogRepository(dataDir)) {
      await seed.AddAsync(CreateRoot("before-start", now.AddHours(-3))).ConfigureAwait(false);
      await seed.AddAsync(CreateRoot("after-start", now.AddHours(-1))).ConfigureAwait(false);
    }

    using var baseApp = new WebApplicationFactory<Program>();
    using var app = baseApp.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
      var existing = s.Where(d => d.ServiceType == typeof(IHostedService)
        && d.ImplementationType == typeof(ExecutionLogRetentionCleanupService)).ToList();
      foreach (var descriptor in existing) {
        s.Remove(descriptor);
      }
      s.AddHostedService(sp => new ExecutionLogRetentionCleanupService(
        sp, sp.GetRequiredService<ILogger<ExecutionLogRetentionCleanupService>>(), processStartUtc));
    }));
    _ = app.CreateClient();
    var repository = app.Services.GetRequiredService<IExecutionLogRepository>();

    await PollUntilStatusAsync(repository, "before-start", "failure").ConfigureAwait(false);
    var after = await repository.GetAsync("after-start").ConfigureAwait(false);
    after.Should().NotBeNull();
    after!.FinalStatus.Should().Be("running");
  }

  [Fact]
  public async Task StartSweepWritesLogLine() {
    var dataDir = TestEnvironment.PrepareCleanDataDir();
    var (rootId, _) = await SeedEarlierRunAsync(dataDir).ConfigureAwait(false);

    using var logs = new TestLoggerProvider();
    using var baseApp = new WebApplicationFactory<Program>();
    using var app = baseApp.WithWebHostBuilder(b => b.ConfigureLogging(logging => {
      logging.AddProvider(logs);
      // This rule is more specific than the runtime logging gate, so the test sees the Information line.
      logging.AddFilter<TestLoggerProvider>(null, LogLevel.Information);
    }));
    _ = app.CreateClient();
    var repository = app.Services.GetRequiredService<IExecutionLogRepository>();

    await PollUntilStatusAsync(repository, rootId, "failure").ConfigureAwait(false);

    var deadline = DateTime.UtcNow + PollLimit;
    while (DateTime.UtcNow < deadline && !logs.Entries.Any(e => e.EventId == 7102)) {
      await Task.Delay(50).ConfigureAwait(false);
    }
    logs.Entries.Where(e => e.EventId == 7102).Should().ContainSingle()
      .Which.Message.Should().Contain("closed 2 running entries");
  }

  private static async Task<(string RootId, string ChildId)> SeedEarlierRunAsync(string dataDir) {
    DateTimeOffset processStart;
    using (var process = Process.GetCurrentProcess()) {
      processStart = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
    }
    var timestamp = processStart.AddDays(-1);
    const string rootId = "sweep-root";
    const string childId = "sweep-child";
    using var seed = new FileExecutionLogRepository(dataDir);
    await seed.AddAsync(CreateRoot(rootId, timestamp)).ConfigureAwait(false);
    await seed.AddAsync(new ExecutionLogEntry {
      Id = childId,
      TimestampUtc = timestamp.AddMinutes(1),
      ExecutionType = "sequence",
      FinalStatus = "running",
      ObjectRef = new ExecutionObjectReference("sequence", "s-farm", "Farm"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/s-farm", null),
      Hierarchy = new ExecutionHierarchyContext(rootId, rootId, 1, 1),
      Summary = "Sequence 'Farm' running.",
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
    }).ConfigureAwait(false);
    return (rootId, childId);
  }

  private static ExecutionLogEntry CreateRoot(string id, DateTimeOffset timestampUtc) => new() {
    Id = id,
    TimestampUtc = timestampUtc,
    ExecutionType = "queue",
    FinalStatus = "running",
    ObjectRef = new ExecutionObjectReference("queue", QueueId, QueueName),
    Navigation = new ExecutionNavigationContext($"/authoring/queues/{QueueId}", null),
    Hierarchy = new ExecutionHierarchyContext(id, null, 0, null),
    Summary = $"Queue '{QueueName}' running.",
    RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
  };

  private static async Task<ExecutionLogEntry> PollUntilStatusAsync(IExecutionLogRepository repository, string id, string status) {
    var deadline = DateTime.UtcNow + PollLimit;
    ExecutionLogEntry? entry = null;
    while (DateTime.UtcNow < deadline) {
      try {
        entry = await repository.GetAsync(id).ConfigureAwait(false);
      }
      catch (IOException) {
        // The sweep can replace the file at this time. Try again.
        entry = null;
      }
      if (entry is not null && string.Equals(entry.FinalStatus, status, StringComparison.Ordinal)) {
        return entry;
      }
      await Task.Delay(50).ConfigureAwait(false);
    }
    entry.Should().NotBeNull();
    entry!.FinalStatus.Should().Be(status, "the start sweep must close entry {0} in {1}", id, PollLimit);
    return entry;
  }
}
